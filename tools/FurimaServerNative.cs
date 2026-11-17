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
    private static HttpListener activeListener = null;
    private static volatile bool isShuttingDown = false;
    private static int cleanupExecuted = 0;
    private static ServerLogWriter logWriter = null;
    private static long dataRevision = 1;
    private static long settingsRevision = 1;
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

    private static long GetItemDeletedAt(Dictionary<string, object> item)
    {
        if (item == null) return 0;
        if (item.ContainsKey("deletedAt") && item["deletedAt"] != null)
        {
            long d = ParseLongSafe(item["deletedAt"], 0);
            if (d > 0) return d;
        }
        if (item.ContainsKey("isDeleted") && item["isDeleted"] != null)
        {
            try
            {
                if (Convert.ToBoolean(item["isDeleted"]))
                {
                    long u = item.ContainsKey("updatedAt") ? ParseLongSafe(item["updatedAt"], 0) : 0;
                    return u > 0 ? u : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }
            catch { }
        }
        return 0;
    }

    private static bool IsItemTrashed(Dictionary<string, object> item)
    {
        return GetItemDeletedAt(item) > 0;
    }

    private static void DeleteItemFolderById(int itemId)
    {
        if (itemId <= 0 || string.IsNullOrEmpty(syncDir) || !Directory.Exists(syncDir)) return;
        try
        {
            foreach (string subDir in Directory.GetDirectories(syncDir))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.StartsWith(itemId + "_") || dirName == itemId.ToString())
                {
                    try
                    {
                        Directory.Delete(subDir, true);
                        Console.WriteLine("  -> [フォルダ物理削除] " + dirName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  -> [警告] フォルダ削除失敗 (" + dirName + "): " + ex.Message);
                    }
                }
            }
        }
        catch { }
    }

    private static string ResolveOrCreateItemFolder(int itemId, string title)
    {
        string safeTitle = string.IsNullOrEmpty(title) ? "商品" : title;
        foreach (char c in Path.GetInvalidFileNameChars()) safeTitle = safeTitle.Replace(c, '_');
        safeTitle = safeTitle.Trim();
        if (string.IsNullOrEmpty(safeTitle)) safeTitle = "商品";

        string desiredDir = Path.Combine(syncDir, itemId + "_" + safeTitle);
        string existingDir = null;
        if (itemId > 0 && Directory.Exists(syncDir))
        {
            foreach (string d in Directory.GetDirectories(syncDir))
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith(itemId + "_") || name == itemId.ToString())
                {
                    existingDir = d;
                    break;
                }
            }
        }

        if (existingDir != null)
        {
            if (!string.Equals(existingDir, desiredDir, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(desiredDir))
            {
                try
                {
                    Directory.Move(existingDir, desiredDir);
                    var keys = new List<string>(hashToFilePathMap.Keys);
                    foreach (var k in keys)
                    {
                        string p = hashToFilePathMap[k];
                        if (!string.IsNullOrEmpty(p) && p.StartsWith(existingDir, StringComparison.OrdinalIgnoreCase))
                        {
                            hashToFilePathMap[k] = desiredDir + p.Substring(existingDir.Length);
                        }
                    }
                    return desiredDir;
                }
                catch
                {
                    return existingDir;
                }
            }
            return existingDir;
        }

        Directory.CreateDirectory(desiredDir);
        return desiredDir;
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

            StartFolderAutoImportWatcher();

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

    private static bool IsSupportedImageExtension(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        string ext = Path.GetExtension(filePath).ToLower();
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".gif" || ext == ".bmp";
    }

    private static bool IsIgnoredAutoImportPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(syncDir)) return true;
        string photoTrashPrefix = Path.Combine(syncDir, "_photo_trash");
        if (fullPath.StartsWith(photoTrashPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        string fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName)) return true;
        if (fileName.StartsWith(".") || fileName.StartsWith("~")) return true;
        string lowerName = fileName.ToLower();
        if (lowerName.EndsWith(".tmp") || lowerName.EndsWith(".part") || lowerName.EndsWith(".crdownload")) return true;
        return false;
    }

    private static bool TryExtractItemIdFromPhotoPath(string fullPath, out int itemId)
    {
        itemId = 0;
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(syncDir)) return false;
        try
        {
            string normSync = Path.GetFullPath(syncDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normFile = Path.GetFullPath(fullPath);
            if (!normFile.StartsWith(normSync + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string rel = normFile.Substring(normSync.Length + 1);
            int sepIdx = rel.IndexOfAny(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            if (sepIdx <= 0) return false;
            string topFolder = rel.Substring(0, sepIdx);
            if (string.IsNullOrEmpty(topFolder) || topFolder.StartsWith("_")) return false;
            int underIdx = topFolder.IndexOf('_');
            string idPart = underIdx > 0 ? topFolder.Substring(0, underIdx) : topFolder;
            int parsed;
            if (int.TryParse(idPart, out parsed) && parsed > 0)
            {
                itemId = parsed;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsValidImageHeader(byte[] bytes, int len)
    {
        if (bytes == null || len < 2) return false;
        if (len >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true; // JPEG
        if (len >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return true; // PNG
        if (len >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true; // WEBP
        if (len >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true; // GIF
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true; // BMP
        return false;
    }

    private static string FindExistingItemFolder(int itemId)
    {
        if (itemId <= 0 || string.IsNullOrEmpty(syncDir) || !Directory.Exists(syncDir)) return null;
        try
        {
            foreach (string d in Directory.GetDirectories(syncDir))
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith(itemId + "_") || name == itemId.ToString())
                {
                    return d;
                }
            }
        }
        catch { }
        return null;
    }

    private static void ScanAndIndexLocalPhotos()
    {
        string targetDir;
        Dictionary<string, PhotoFileCacheEntry> prevCache;
        lock (lockObj)
        {
            targetDir = syncDir;
            prevCache = new Dictionary<string, PhotoFileCacheEntry>(photoStatCache, StringComparer.OrdinalIgnoreCase);
        }

        var nextMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nextCache = new Dictionary<string, PhotoFileCacheEntry>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
        {
            string[] allFiles = null;
            try
            {
                allFiles = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                allFiles = new string[0];
            }

            using (var md5 = MD5.Create())
            {
                foreach (var file in allFiles)
                {
                    if (IsIgnoredAutoImportPath(file)) continue;
                    if (!IsSupportedImageExtension(file)) continue;
                    try
                    {
                        FileInfo fi = new FileInfo(file);
                        if (!fi.Exists || fi.Length <= 0) continue;

                        long len = fi.Length;
                        long ticks = fi.LastWriteTimeUtc.Ticks;
                        string hashStr = null;

                        PhotoFileCacheEntry cached;
                        if (prevCache.TryGetValue(file, out cached) &&
                            cached != null &&
                            cached.length == len &&
                            cached.lastWriteUtcTicks == ticks &&
                            !string.IsNullOrEmpty(cached.md5))
                        {
                            hashStr = cached.md5;
                        }
                        else
                        {
                            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                byte[] hashBytes = md5.ComputeHash(stream);
                                hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                            }
                        }

                        if (!string.IsNullOrEmpty(hashStr))
                        {
                            nextCache[file] = new PhotoFileCacheEntry
                            {
                                length = len,
                                lastWriteUtcTicks = ticks,
                                md5 = hashStr
                            };
                            if (!nextMap.ContainsKey(hashStr))
                            {
                                nextMap[hashStr] = file;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        lock (lockObj)
        {
            hashToFilePathMap = nextMap;
            photoStatCache = nextCache;
        }
        Console.WriteLine("インデックス完了: " + nextMap.Count + "件のユニーク写真ハッシュをロードしました。");
    }

    private static void StartFolderAutoImportWatcher()
    {
        try
        {
            if (string.IsNullOrEmpty(syncDir) || !Directory.Exists(syncDir)) return;
            StopFolderAutoImportWatcher();

            FileSystemWatcher watcher = new FileSystemWatcher(syncDir);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            try { watcher.InternalBufferSize = 65536; } catch { }

            watcher.Created += (s, e) => EnqueueWatchedFile(e.FullPath);
            watcher.Changed += (s, e) => EnqueueWatchedFile(e.FullPath);
            watcher.Renamed += (s, e) => EnqueueWatchedFile(e.FullPath);
            watcher.Error += (s, e) =>
            {
                if (isShuttingDown) return;
                Console.WriteLine("[自動取込] 監視バッファ警告を検知したため軽量差分スキャンを実行します。");
                TriggerFallbackFolderRescan();
            };

            watcher.EnableRaisingEvents = true;
            folderWatcher = watcher;
            watcherFlushTimer = new System.Threading.Timer(OnWatcherTimerTick, null, 500, 500);
            Console.WriteLine("[自動取込] フォルダ監視を開始しました: " + syncDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[自動取込] フォルダ監視の開始に失敗しました: " + ex.Message);
        }
    }

    private static void StopFolderAutoImportWatcher()
    {
        System.Threading.Timer t = watcherFlushTimer;
        watcherFlushTimer = null;
        if (t != null)
        {
            try { t.Dispose(); } catch { }
        }

        FileSystemWatcher w = folderWatcher;
        folderWatcher = null;
        if (w != null)
        {
            try { w.EnableRaisingEvents = false; } catch { }
            try { w.Dispose(); } catch { }
        }

        lock (watcherQueueLock)
        {
            pendingWatchFiles.Clear();
            pendingWatchRetries.Clear();
        }
    }

    private static void EnqueueWatchedFile(string fullPath)
    {
        if (isShuttingDown || string.IsNullOrEmpty(fullPath)) return;
        if (IsIgnoredAutoImportPath(fullPath)) return;
        if (!IsSupportedImageExtension(fullPath)) return;
        int dummyId;
        if (!TryExtractItemIdFromPhotoPath(fullPath, out dummyId)) return;

        lock (watcherQueueLock)
        {
            pendingWatchFiles[fullPath] = DateTime.UtcNow.Ticks;
        }
    }

    private static void OnWatcherTimerTick(object state)
    {
        if (isShuttingDown) return;
        ProcessPendingWatchFiles();
    }

    private static void TriggerFallbackFolderRescan()
    {
        if (isShuttingDown) return;
        if (Interlocked.CompareExchange(ref watcherRescanRunning, 1, 0) != 0) return;

        ThreadPool.QueueUserWorkItem((o) =>
        {
            try
            {
                if (isShuttingDown) return;
                List<string> newPaths = new List<string>();
                string targetDir;
                Dictionary<string, PhotoFileCacheEntry> cacheSnapshot;
                lock (lockObj)
                {
                    targetDir = syncDir;
                    cacheSnapshot = new Dictionary<string, PhotoFileCacheEntry>(photoStatCache, StringComparer.OrdinalIgnoreCase);
                }

                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    foreach (string file in Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories))
                    {
                        if (isShuttingDown) break;
                        if (IsIgnoredAutoImportPath(file) || !IsSupportedImageExtension(file)) continue;
                        int dummyId;
                        if (!TryExtractItemIdFromPhotoPath(file, out dummyId)) continue;
                        if (!cacheSnapshot.ContainsKey(file))
                        {
                            newPaths.Add(file);
                        }
                    }
                }

                if (newPaths.Count > 0)
                {
                    lock (watcherQueueLock)
                    {
                        long nowTicks = DateTime.UtcNow.Ticks - TimeSpan.FromMilliseconds(500).Ticks;
                        foreach (string p in newPaths)
                        {
                            if (!pendingWatchFiles.ContainsKey(p))
                            {
                                pendingWatchFiles[p] = nowTicks;
                            }
                        }
                    }
                    ProcessPendingWatchFiles();
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref watcherRescanRunning, 0);
            }
        });
    }

    private static void ProcessPendingWatchFiles()
    {
        if (isShuttingDown) return;
        if (Interlocked.CompareExchange(ref watcherFlushRunning, 1, 0) != 0) return;

        try
        {
            long readyCutoff = DateTime.UtcNow.Ticks - TimeSpan.FromMilliseconds(400).Ticks;
            List<string> readyPaths = new List<string>();

            lock (watcherQueueLock)
            {
                if (pendingWatchFiles.Count == 0) return;
                foreach (var kv in pendingWatchFiles)
                {
                    if (kv.Value <= readyCutoff)
                    {
                        readyPaths.Add(kv.Key);
                    }
                }
                foreach (string p in readyPaths)
                {
                    pendingWatchFiles.Remove(p);
                }
            }

            if (readyPaths.Count == 0) return;

            List<PendingAutoImportPhoto> validatedList = new List<PendingAutoImportPhoto>();
            List<string> retryPaths = new List<string>();

            using (var md5 = MD5.Create())
            {
                foreach (string filePath in readyPaths)
                {
                    if (isShuttingDown) break;
                    if (!File.Exists(filePath) || IsIgnoredAutoImportPath(filePath) || !IsSupportedImageExtension(filePath))
                    {
                        lock (watcherQueueLock) { pendingWatchRetries.Remove(filePath); }
                        continue;
                    }

                    int itemId;
                    if (!TryExtractItemIdFromPhotoPath(filePath, out itemId))
                    {
                        lock (watcherQueueLock) { pendingWatchRetries.Remove(filePath); }
                        continue;
                    }

                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        if (!fi.Exists || fi.Length <= 0)
                        {
                            retryPaths.Add(filePath);
                            continue;
                        }

                        long len = fi.Length;
                        long ticks = fi.LastWriteTimeUtc.Ticks;

                        PhotoFileCacheEntry cachedEntry = null;
                        lock (lockObj)
                        {
                            photoStatCache.TryGetValue(filePath, out cachedEntry);
                        }
                        if (cachedEntry != null && cachedEntry.length == len && cachedEntry.lastWriteUtcTicks == ticks && !string.IsNullOrEmpty(cachedEntry.md5))
                        {
                            validatedList.Add(new PendingAutoImportPhoto
                            {
                                filePath = filePath,
                                itemId = itemId,
                                md5 = cachedEntry.md5,
                                length = len,
                                lastWriteUtcTicks = ticks
                            });
                            lock (watcherQueueLock) { pendingWatchRetries.Remove(filePath); }
                            continue;
                        }

                        byte[] header = new byte[16];
                        int headerRead = 0;
                        string hashStr = "";
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            headerRead = fs.Read(header, 0, header.Length);
                            if (!IsValidImageHeader(header, headerRead))
                            {
                                lock (watcherQueueLock) { pendingWatchRetries.Remove(filePath); }
                                continue;
                            }
                            fs.Position = 0;
                            byte[] hashBytes = md5.ComputeHash(fs);
                            hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                        }

                        if (!string.IsNullOrEmpty(hashStr))
                        {
                            validatedList.Add(new PendingAutoImportPhoto
                            {
                                filePath = filePath,
                                itemId = itemId,
                                md5 = hashStr,
                                length = len,
                                lastWriteUtcTicks = ticks
                            });
                            lock (watcherQueueLock) { pendingWatchRetries.Remove(filePath); }
                        }
                    }
                    catch (IOException)
                    {
                        retryPaths.Add(filePath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        retryPaths.Add(filePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[自動取込] ファイル検証エラー (" + Path.GetFileName(filePath) + "): " + ex.Message);
                        lock (watcherQueueLock) { pendingWatchRetries.Remove(filePath); }
                    }
                }
            }

            if (retryPaths.Count > 0)
            {
                lock (watcherQueueLock)
                {
                    long nowTicks = DateTime.UtcNow.Ticks;
                    foreach (string rp in retryPaths)
                    {
                        int count = 0;
                        pendingWatchRetries.TryGetValue(rp, out count);
                        if (count < 12)
                        {
                            pendingWatchRetries[rp] = count + 1;
                            if (!pendingWatchFiles.ContainsKey(rp))
                            {
                                pendingWatchFiles[rp] = nowTicks;
                            }
                        }
                        else
                        {
                            pendingWatchRetries.Remove(rp);
                        }
                    }
                }
            }

            if (validatedList.Count > 0)
            {
                int importedCount = CommitAutoImportedPhotosBatch(validatedList);
                if (importedCount > 0)
                {
                    Console.WriteLine("[自動取込] フォルダ監視から " + importedCount + " 枚の新規写真を取り込みました。");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[自動取込] バッチ処理エラー: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref watcherFlushRunning, 0);
        }
    }

    private static int CommitAutoImportedPhotosBatch(List<PendingAutoImportPhoto> entries)
    {
        if (entries == null || entries.Count == 0) return 0;

        int importedCount = 0;
        int indexedCount = 0;
        int missingCount = 0;
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (lockObj)
        {
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.md5)) continue;

                photoStatCache[entry.filePath] = new PhotoFileCacheEntry
                {
                    length = entry.length,
                    lastWriteUtcTicks = entry.lastWriteUtcTicks,
                    md5 = entry.md5
                };
                if (!hashToFilePathMap.ContainsKey(entry.md5) || !File.Exists(hashToFilePathMap[entry.md5]))
                {
                    hashToFilePathMap[entry.md5] = entry.filePath;
                }

                var item = itemsDb.Find(i => i != null && i.ContainsKey("id") && ParseIntSafe(i["id"], 0) == entry.itemId);
                if (item == null || IsItemTrashed(item)) continue;

                var photosList = item.ContainsKey("photos") && item["photos"] != null
                    ? SyncStore.NormalizePhotosList(item["photos"])
                    : new List<Dictionary<string, object>>();

                bool exists = false;
                foreach (var p in photosList)
                {
                    if (p == null) continue;
                    string h = p.ContainsKey("md5Hash") && p["md5Hash"] != null ? p["md5Hash"].ToString().Trim() : "";
                    if (string.Equals(h, entry.md5, StringComparison.OrdinalIgnoreCase))
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
                        { "itemId", entry.itemId },
                        { "photoUrl", "/api/photos/" + entry.md5 },
                        { "md5Hash", entry.md5 },
                        { "displayOrder", photosList.Count }
                    });
                    item["photos"] = photosList;
                    item["updatedAt"] = nowMs;
                    item["syncedToPhone"] = false;
                    importedCount++;
                }
            }

            if (importedCount > 0)
            {
                BumpDataRevision();
            }
            indexedCount = hashToFilePathMap.Count;
            missingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
        }

        if (importedCount > 0)
        {
            SaveItemsDbToDisk();
            if (qrForm != null && !isShuttingDown)
            {
                try { qrForm.UpdateStatusInfo(indexedCount, missingCount); } catch { }
            }
        }

        return importedCount;
    }

    private static void LoadItemsDbFromDisk()
    {
        bool changed = false;
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

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long cutoffMs = nowMs - (30L * 24L * 60L * 60L * 1000L);
            HashSet<int> purgeIds = new HashSet<int>();
            List<string> purgeUuids = new List<string>();

            foreach (var item in itemsDb)
            {
                if (item == null) continue;
                bool hadUuid = item.ContainsKey("syncUuid") && item["syncUuid"] != null && !string.IsNullOrEmpty(item["syncUuid"].ToString().Trim());
                string uuid = EnsureItemSyncUuid(item);
                if (!hadUuid) changed = true;

                bool legacyDeleted = false;
                if (item.ContainsKey("isDeleted") && item["isDeleted"] != null)
                {
                    try { legacyDeleted = Convert.ToBoolean(item["isDeleted"]); } catch { }
                }

                long deletedAtVal = item.ContainsKey("deletedAt") ? ParseLongSafe(item["deletedAt"], 0) : 0;
                if (legacyDeleted && deletedAtVal <= 0)
                {
                    long fallbackTime = item.ContainsKey("updatedAt") ? ParseLongSafe(item["updatedAt"], nowMs) : nowMs;
                    deletedAtVal = fallbackTime > 0 ? fallbackTime : nowMs;
                    item["deletedAt"] = deletedAtVal;
                    changed = true;
                }
                if (deletedAtVal > 0 && !legacyDeleted)
                {
                    item["isDeleted"] = true;
                    changed = true;
                }
                if (deletedAtVal <= 0 && !item.ContainsKey("deletedAt"))
                {
                    item["deletedAt"] = null;
                }

                if (deletedAtVal > 0 && deletedAtVal < cutoffMs)
                {
                    int id = item.ContainsKey("id") ? ParseIntSafe(item["id"], 0) : 0;
                    if (id > 0) purgeIds.Add(id);
                    if (!string.IsNullOrEmpty(uuid)) purgeUuids.Add(uuid);
                }
            }

            // 期限切れ親まとめ商品の子商品も一括削除対象に含める
            if (purgeIds.Count > 0)
            {
                foreach (var item in itemsDb)
                {
                    if (item == null) continue;
                    int parentId = item.ContainsKey("mergedParentId") ? ParseIntSafe(item["mergedParentId"], 0) : 0;
                    if (parentId > 0 && purgeIds.Contains(parentId))
                    {
                        int cid = item.ContainsKey("id") ? ParseIntSafe(item["id"], 0) : 0;
                        string cuuid = EnsureItemSyncUuid(item);
                        if (cid > 0) purgeIds.Add(cid);
                        if (!string.IsNullOrEmpty(cuuid) && !purgeUuids.Contains(cuuid)) purgeUuids.Add(cuuid);
                    }
                }

                foreach (int pid in purgeIds)
                {
                    DeleteItemFolderById(pid);
                }
                SyncStore.RecordPermanentlyDeletedUuids(purgeUuids);
                itemsDb.RemoveAll(i => i != null && i.ContainsKey("id") && purgeIds.Contains(ParseIntSafe(i["id"], 0)));
                changed = true;
                Console.WriteLine("[ゴミ箱自動整理] 30日経過アイテムを完全削除しました (" + purgeIds.Count + "件)");
            }

            Console.WriteLine("ロード完了: " + itemsDb.Count + "件の商品メタデータを保持しています。");
        }

        if (changed)
        {
            SaveItemsDbToDisk();
            ScanAndIndexLocalPhotos();
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

    // フェーズ 6-4: 売却系ステータス {SOLD, SHIPPING} の共通判定。
    // 売却価格・売却日時の保持、利益計算、売上集計はすべてこの所属判定に揃える。
    private static bool IsSoldFamily(string status)
    {
        return status == "SOLD" || status == "SHIPPING";
    }

    // フェーズ 6-4: ステータス表示名の統一表。不明なコードは出品中へ倒さずコードそのものを返す。
    // MERGED は親ステータスから決まるため、ここでは扱わない (GetMergedChildStatusLabel を使う)。
    private static string GetStatusLabel(string status)
    {
        if (status == "DRAFT") return "下書き中";
        if (status == "DRAFT_DONE") return "下書き済み";
        if (status == "LISTED") return "出品中";
        if (status == "SHIPPING") return "発送準備中";
        if (status == "SOLD") return "売却済み";
        return status ?? "";
    }

    // フェーズ 6-4: まとめ子商品の表示名は親のステータスから決める。
    private static string GetMergedChildStatusLabel(string parentStatus)
    {
        if (parentStatus == "DRAFT") return "まとめ下書き中";
        if (parentStatus == "DRAFT_DONE") return "まとめ下書き済み";
        if (parentStatus == "SHIPPING") return "まとめ発送準備中";
        if (parentStatus == "SOLD") return "まとめ売却済";
        return "まとめ出品中";
    }

    // レビュー 24-1: トンネル (cloudflared) 経由のリクエストには転送ヘッダが付く。
    // ループバック免除と管理画面の配信を「直接のローカルアクセス」だけに限定するための判定。
    private static bool HasForwardedHeaders(HttpListenerRequest req)
    {
        return !string.IsNullOrEmpty(req.Headers["Cf-Connecting-Ip"]) ||
               !string.IsNullOrEmpty(req.Headers["Cf-Ray"]) ||
               !string.IsNullOrEmpty(req.Headers["X-Forwarded-For"]) ||
               !string.IsNullOrEmpty(req.Headers["Forwarded"]) ||
               !string.IsNullOrEmpty(req.Headers["X-Real-IP"]);
    }

    private static bool AuthenticateRequest(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;

        if (req.HttpMethod == "OPTIONS") return true;

        IPAddress remoteIp = req.RemoteEndPoint.Address;
        bool isLoopback = IPAddress.IsLoopback(remoteIp);
        bool forwarded = HasForwardedHeaders(req);

        string path = req.Url.AbsolutePath;

        // レビュー 24-1 (方式1): 管理画面 HTML は合鍵そのものを含むため PC ローカル専用とする。
        // 転送ヘッダ付き (トンネル経由) またはループバック以外からの画面取得は拒否する。
        if (path == "/")
        {
            if (forwarded || !isLoopback)
            {
                res.StatusCode = 403;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "管理画面は PC ローカルからのみ利用できます" } });
                return false;
            }
            return true;
        }

        // ループバックからの直接アクセスは許可 (トンネル経由は転送ヘッダで除外する)
        if (isLoopback && !forwarded) return true;
        // フェーズ 6-3: 変化カウンタの公開窓口のみ認証を免除する (GET + 完全一致パスのみ)。
        // 見張りスクリプトはこの数字だけを見て、認証情報を持たない。
        if (req.HttpMethod == "GET" && path == "/api/claude/revision")
        {
            return true;
        }

        // フェーズ 6-3 (案2): 許可アドレス (Muse 端末) からの対象エンドポイントだけ Bearer を免除する。
        // 免除するのは Bearer 層のみで、ジョブトークン等の検証は各ハンドラで従来どおり行う。
        if (IsTrustedClaudeExemptRequest(req, path))
        {
            return true;
        }

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

    // フェーズ 6-3 (案2 / レビュー 23-1): Bearer 免除の対象は、メソッドとパスで厳密に一致する
    // 次のエンドポイントだけに限る。一覧 (GET)、詳細 (GET {jobId})、写真 (GET {jobId}/photos/{md5})、
    // 実行系 (POST {jobId}/results・errors・complete)。作成・承認・移動・確認・取消は免除しない。
    private static bool IsTrustedClaudeExemptPath(string method, string path)
    {
        if (path == "/api/claude/jobs") return method == "GET";
        string prefix = "/api/claude/jobs/";
        if (!path.StartsWith(prefix)) return false;
        string rest = path.Substring(prefix.Length);
        string[] parts = rest.Split('/');
        if (method == "GET")
        {
            if (parts.Length == 1 && parts[0].Length > 0) return true;
            if (parts.Length == 3 && parts[0].Length > 0 && parts[1] == "photos" && parts[2].Length > 0) return true;
            return false;
        }
        if (method == "POST")
        {
            if (parts.Length == 2 && parts[0].Length > 0 && (parts[1] == "results" || parts[1] == "errors" || parts[1] == "complete")) return true;
            return false;
        }
        return false;
    }

    // 送信元が許可アドレスと一致するか。判定は RemoteEndPoint のみで行い、転送ヘッダは使わない。
    // IPv4-mapped IPv6 は IPv4 へ正規化してから比較する。
    private static bool IsTrustedClaudeExemptRequest(HttpListenerRequest req, string path)
    {
        // レビュー 24-1 補足ガード: 転送ヘッダを持つリクエストは免除対象にしない (防御的措置)
        if (HasForwardedHeaders(req)) return false;
        if (!IsTrustedClaudeExemptPath(req.HttpMethod, path)) return false;
        string configured = SyncStore.GetTrustedMuseIp();
        if (string.IsNullOrEmpty(configured)) return false;
        IPAddress allowed;
        if (!IPAddress.TryParse(configured, out allowed)) return false;
        IPAddress remote = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address : null;
        if (remote == null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (allowed.IsIPv4MappedToIPv6) allowed = allowed.MapToIPv4();
        return remote.Equals(allowed);
    }

    // 許可アドレスの設定値の検証と正規化。空欄は「免除オフ」として有効。
    // 単一の IP のみ受理し、ループバックと未指定アドレス (0.0.0.0 / ::) は危険なため拒否する。
    private static bool TryNormalizeTrustedIp(string raw, out string normalized)
    {
        normalized = "";
        if (raw == null) raw = "";
        raw = raw.Trim();
        if (raw.Length == 0) return true;
        IPAddress ip;
        if (raw.IndexOf('.') >= 0 && raw.IndexOf(':') < 0)
        {
            // レビュー 24-5: IPv4 は 4 オクテットの正規形のみ受理する。
            // IPAddress.TryParse は "100" や "192.168.1" のような省略表記も受理してしまうため、先に分解して検証する。
            string[] parts = raw.Split('.');
            if (parts.Length != 4) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part.Length > 3) return false;
                if (part.Length > 1 && part[0] == '0') return false;
                int value = 0;
                for (int k = 0; k < part.Length; k++)
                {
                    char c = part[k];
                    if (c < '0' || c > '9') return false;
                    value = value * 10 + (c - '0');
                }
                if (value > 255) return false;
            }
            if (!IPAddress.TryParse(raw, out ip)) return false;
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        }
        else if (raw.IndexOf(':') >= 0)
        {
            if (!IPAddress.TryParse(raw, out ip)) return false;
            if (ip.IsIPv4MappedToIPv6)
            {
                // IPv4-mapped IPv6 は IPv4 のドット表記として検証し直す
                return TryNormalizeTrustedIp(ip.MapToIPv4().ToString(), out normalized);
            }
            // IPv6 は正規形 (ToString の往復一致) のみ受理する
            if (!string.Equals(ip.ToString(), raw, StringComparison.OrdinalIgnoreCase)) return false;
        }
        else
        {
            return false;
        }
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.Equals(IPAddress.Broadcast)) return false;
        byte[] addrBytes = ip.GetAddressBytes();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (addrBytes[0] >= 224 && addrBytes[0] <= 239) return false;
        }
        else
        {
            if (addrBytes[0] == 0xFF) return false;
        }
        normalized = ip.ToString();
        return true;
    }

    private static void HandleUpdateTrustedMuseIp(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            string raw = "";
            if (!string.IsNullOrEmpty(jsonStr))
            {
                JObject json = null;
                try
                {
                    json = JObject.Parse(jsonStr);
                }
                catch (Exception)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "リクエスト形式が不正です" } });
                    return;
                }
                JToken ipToken = json["trustedMuseIp"];
                if (ipToken != null && ipToken.Type != JTokenType.String && ipToken.Type != JTokenType.Null)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "許可アドレスは文字列で指定してください" } });
                    return;
                }
                raw = (ipToken != null && ipToken.Type == JTokenType.String) ? (string)ipToken : "";
            }
            string normalized;
            if (!TryNormalizeTrustedIp(raw, out normalized))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "許可アドレスの形式が不正です（単一のIPアドレスを指定してください）" } });
                return;
            }
            if (!SyncStore.SetTrustedMuseIp(normalized))
            {
                res.StatusCode = 500;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "許可アドレスの保存に失敗しました" } });
                return;
            }
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "trustedMuseIp", normalized } });
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleRequest(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;

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

        string rawPath = req.Url.AbsolutePath;

        if (rawPath != "/api/server/heartbeat" && rawPath != "/api/claude/revision")
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
                SendJson(res, new Dictionary<string, object>
                {
                    { "claudeRevision", GetClaudeRevision() }
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
                bool isConnected = (DateTime.Now - lastHeartbeat).TotalSeconds < 25;
                int missingCount;
                int trashCount = 0;
                int indexedPhotosCount = 0;
                int totalItemsCount = 0;
                lock (lockObj)
                {
                    indexedPhotosCount = hashToFilePathMap.Count;
                    totalItemsCount = itemsDb.Count;
                    missingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
                    foreach (var item in itemsDb)
                    {
                        if (IsItemTrashed(item)) trashCount++;
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
                ServePhotoTrashImage(res, trashId);
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
            try { res.StatusCode = 500; } catch { }
            try { res.Close(); } catch { }
        }
    }

    private static void HandleSyncItems(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);

                string reqStoreId = json.Value<string>("storeId") ?? "";
                int protocolVersion = ParseIntSafe(json["protocolVersion"], 3);
                JArray itemsArr = json["items"] as JArray;
                JArray clientDeletedUuidsArr = json["permanentlyDeletedUuids"] as JArray;

                List<string> syncedHashes = new List<string>();
                List<string> neededHashes = new List<string>();
                bool dataChanged = false;

                lock (lockObj)
                {
                    // 1. スマホから送信された完全削除UUID（tombstones）を受理して物理削除
                    if (clientDeletedUuidsArr != null && clientDeletedUuidsArr.Count > 0)
                    {
                        var incomingDeletedUuids = new List<string>();
                        foreach (var tok in clientDeletedUuidsArr)
                        {
                            string u = (tok != null ? tok.ToString() : "").Trim();
                            if (!string.IsNullOrEmpty(u)) incomingDeletedUuids.Add(u);
                        }
                        if (incomingDeletedUuids.Count > 0)
                        {
                            SyncStore.RecordPermanentlyDeletedUuids(incomingDeletedUuids);
                            var delUuidSet = new HashSet<string>(incomingDeletedUuids, StringComparer.OrdinalIgnoreCase);
                            var idsToDelete = new HashSet<int>();
                            foreach (var existing in itemsDb)
                            {
                                if (existing == null) continue;
                                string eu = EnsureItemSyncUuid(existing);
                                if (delUuidSet.Contains(eu))
                                {
                                    int eid = ParseIntSafe(existing["id"], 0);
                                    if (eid > 0) idsToDelete.Add(eid);
                                }
                            }
                            // 削除対象の親に紐づく子商品も削除
                            if (idsToDelete.Count > 0)
                            {
                                foreach (var candidate in itemsDb)
                                {
                                    if (candidate == null) continue;
                                    int mPid = candidate.ContainsKey("mergedParentId") ? ParseIntSafe(candidate["mergedParentId"], 0) : 0;
                                    if (mPid > 0 && idsToDelete.Contains(mPid))
                                    {
                                        int cid = ParseIntSafe(candidate["id"], 0);
                                        if (cid > 0) idsToDelete.Add(cid);
                                        string cu = EnsureItemSyncUuid(candidate);
                                        if (!string.IsNullOrEmpty(cu)) SyncStore.RecordPermanentlyDeletedUuid(cu);
                                    }
                                }
                                foreach (int did in idsToDelete)
                                {
                                    DeleteItemFolderById(did);
                                }
                                itemsDb.RemoveAll(i => i != null && i.ContainsKey("id") && idsToDelete.Contains(ParseIntSafe(i["id"], 0)));
                                dataChanged = true;
                            }
                        }
                    }

                    if (itemsArr != null)
                    {
                        // スマホ側アイテムリストから親先に並べる（mergedParentId解決用）
                        var sortedPhoneItems = new List<JObject>();
                        var phoneIdToUuid = new Dictionary<int, string>();
                        foreach (JObject obj in itemsArr)
                        {
                            if (obj == null) continue;
                            int pid = ParseIntSafe(obj["id"], 0);
                            string puuid = (obj.Value<string>("syncUuid") ?? "").Trim();
                            if (pid > 0 && !string.IsNullOrEmpty(puuid))
                            {
                                phoneIdToUuid[pid] = puuid;
                            }
                            sortedPhoneItems.Add(obj);
                        }
                        sortedPhoneItems.Sort((a, b) =>
                        {
                            int pa = (a["mergedParentId"] != null && a["mergedParentId"].Type != JTokenType.Null && ParseIntSafe(a["mergedParentId"], 0) > 0) ? 1 : 0;
                            int pb = (b["mergedParentId"] != null && b["mergedParentId"].Type != JTokenType.Null && ParseIntSafe(b["mergedParentId"], 0) > 0) ? 1 : 0;
                            return pa.CompareTo(pb);
                        });

                        // syncUuid -> PC上itemsDb内インデックス/エントリの照合マップを構築
                        int maxPcId = 0;
                        foreach (var i in itemsDb)
                        {
                            if (i == null) continue;
                            int eid = ParseIntSafe(i["id"], 0);
                            if (eid > maxPcId) maxPcId = eid;
                            EnsureItemSyncUuid(i);
                        }

                        var uuidToAssignedPcId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var i in itemsDb)
                        {
                            if (i == null) continue;
                            string eu = EnsureItemSyncUuid(i);
                            int eid = ParseIntSafe(i["id"], 0);
                            if (!string.IsNullOrEmpty(eu) && eid > 0)
                            {
                                uuidToAssignedPcId[eu] = eid;
                            }
                        }

                        foreach (JObject item in sortedPhoneItems)
                        {
                            int phoneId = ParseIntSafe(item["id"], 0);
                            string incomingUuid = (item.Value<string>("syncUuid") ?? "").Trim();

                            if (!string.IsNullOrEmpty(incomingUuid) && SyncStore.IsUuidPermanentlyDeleted(incomingUuid))
                            {
                                // サーバー側で既に完全削除済みのためゾンビ復活を抑止
                                continue;
                            }

                            Dictionary<string, object> existingByUuid = null;
                            if (!string.IsNullOrEmpty(incomingUuid))
                            {
                                existingByUuid = itemsDb.Find(i => i != null && string.Equals(EnsureItemSyncUuid(i), incomingUuid, StringComparison.OrdinalIgnoreCase));
                            }

                            Dictionary<string, object> existingById = null;
                            if (phoneId > 0)
                            {
                                existingById = itemsDb.Find(i => i != null && i.ContainsKey("id") && ParseIntSafe(i["id"], 0) == phoneId);
                            }

                            Dictionary<string, object> existingItem = existingByUuid;
                            bool isIdCollision = false;
                            if (existingItem == null && existingById != null)
                            {
                                string pcExistingUuid = EnsureItemSyncUuid(existingById);
                                if (string.IsNullOrEmpty(incomingUuid) || string.Equals(pcExistingUuid, incomingUuid, StringComparison.OrdinalIgnoreCase))
                                {
                                    existingItem = existingById;
                                }
                                else
                                {
                                    isIdCollision = true;
                                }
                            }

                            long phoneUpdatedAt = ParseLongSafe(item["updatedAt"], 0);
                            if (existingItem != null)
                            {
                                bool syncedToPhone = !existingItem.ContainsKey("syncedToPhone") || existingItem["syncedToPhone"] == null || Convert.ToBoolean(existingItem["syncedToPhone"]);
                                long pcUpdatedAt = existingItem.ContainsKey("updatedAt") ? ParseLongSafe(existingItem["updatedAt"], 0) : 0;
                                if (!syncedToPhone && pcUpdatedAt > phoneUpdatedAt)
                                {
                                    // スマホ未PULLのPC側更新の方が新しいため保護
                                    continue;
                                }
                            }

                            int assignedId = existingItem != null
                                ? ParseIntSafe(existingItem["id"], phoneId)
                                : (isIdCollision || existingById != null ? (++maxPcId) : (phoneId > 0 ? phoneId : (++maxPcId)));

                            string resolvedUuid = !string.IsNullOrEmpty(incomingUuid)
                                ? incomingUuid
                                : (existingItem != null ? EnsureItemSyncUuid(existingItem) : Guid.NewGuid().ToString());
                            uuidToAssignedPcId[resolvedUuid] = assignedId;

                            string title = item.Value<string>("title") ?? "商品";
                            string description = item.Value<string>("description") ?? "";
                            int price = ParseIntSafe(item["price"], 0);
                            int profit = ParseIntSafe(item["profit"], 0);
                            string itemStatus = item.Value<string>("status") ?? "LISTED";
                            long incomingDeletedAt = item["deletedAt"] != null && item["deletedAt"].Type != JTokenType.Null
                                ? ParseLongSafe(item["deletedAt"], 0)
                                : 0;
                            bool isTrashed = incomingDeletedAt > 0;
                            object resolvedDeletedAtObj = isTrashed ? (object)incomingDeletedAt : null;
                            string statusLabel = isTrashed
                                ? "ゴミ箱"
                                : (item.Value<string>("statusLabel") ?? GetStatusLabel(itemStatus));

                            string itemDirPath = ResolveOrCreateItemFolder(assignedId, title);

                            string txtPath = Path.Combine(itemDirPath, "商品情報_説明文.txt");
                            string txtContent = "【商品ID】 " + assignedId + "\n【タイトル】 " + title + "\n【販売価格】 ¥" + price.ToString("N0") + "\n【純利益】 ¥" + profit.ToString("N0") + "\n【ステータス】 " + statusLabel + "\n\n--- 商品説明 ---\n" + description;
                            File.WriteAllText(txtPath, txtContent, Encoding.UTF8);

                            JArray photosArr = item["photos"] as JArray;
                            List<Dictionary<string, object>> savedPhotosList = new List<Dictionary<string, object>>();

                            if (photosArr != null)
                            {
                                foreach (JObject photo in photosArr)
                                {
                                    int pId = ParseIntSafe(photo["id"], 0);
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

                                    var photoEntry = new Dictionary<string, object>
                                    {
                                        { "id", pId },
                                        { "md5Hash", md5Hash },
                                        { "photoUrl", "/api/photos/" + md5Hash },
                                        { "displayOrder", ParseIntSafe(photo["displayOrder"], savedPhotosList.Count) }
                                    };
                                    int childItemIdVal = ParseIntSafe(photo["childItemId"], 0);
                                    if (childItemIdVal > 0)
                                    {
                                        photoEntry["childItemId"] = childItemIdVal;
                                    }
                                    savedPhotosList.Add(photoEntry);
                                }
                            }

                            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            long phoneCreatedAt = ParseLongSafe(item["createdAt"], 0);
                            long existingCreatedAt = existingItem != null && existingItem.ContainsKey("createdAt") ? ParseLongSafe(existingItem["createdAt"], 0) : 0;
                            long resolvedCreatedAt = phoneCreatedAt > 0
                                ? (existingCreatedAt > 0 ? Math.Min(phoneCreatedAt, existingCreatedAt) : phoneCreatedAt)
                                : (existingCreatedAt > 0 ? existingCreatedAt : nowMs);

                            long existingUpdatedAt = existingItem != null && existingItem.ContainsKey("updatedAt") ? ParseLongSafe(existingItem["updatedAt"], 0) : 0;
                            long resolvedUpdatedAt = Math.Max(phoneUpdatedAt, existingUpdatedAt);
                            if (resolvedUpdatedAt <= 0) resolvedUpdatedAt = resolvedCreatedAt;

                            long phoneSoldAt = ParseLongSafe(item["soldAt"], 0);
                            long existingSoldAt = existingItem != null && existingItem.ContainsKey("soldAt") ? ParseLongSafe(existingItem["soldAt"], 0) : 0;
                            object resolvedSoldAt = null;
                            if (IsSoldFamily(itemStatus))
                            {
                                long sAt = phoneSoldAt > 0 ? phoneSoldAt : existingSoldAt;
                                if (sAt > 0) resolvedSoldAt = sAt;
                            }

                            // まとめ親IDの解決 (syncUuid優先で番号衝突時にも追従)
                            object resolvedMergedParentIdObj = null;
                            string parentSyncUuid = (item.Value<string>("mergedParentSyncUuid") ?? "").Trim();
                            if (string.IsNullOrEmpty(parentSyncUuid) && item["mergedParentId"] != null && item["mergedParentId"].Type != JTokenType.Null)
                            {
                                int rawPid = ParseIntSafe(item["mergedParentId"], 0);
                                if (rawPid > 0 && phoneIdToUuid.ContainsKey(rawPid))
                                {
                                    parentSyncUuid = phoneIdToUuid[rawPid];
                                }
                            }
                            if (!string.IsNullOrEmpty(parentSyncUuid) && uuidToAssignedPcId.ContainsKey(parentSyncUuid))
                            {
                                resolvedMergedParentIdObj = uuidToAssignedPcId[parentSyncUuid];
                            }
                            else if (item["mergedParentId"] != null && item["mergedParentId"].Type != JTokenType.Null)
                            {
                                int rawPid = ParseIntSafe(item["mergedParentId"], 0);
                                if (rawPid > 0) resolvedMergedParentIdObj = rawPid;
                            }

                            var newItemDict = new Dictionary<string, object>
                            {
                                { "id", assignedId },
                                { "syncUuid", resolvedUuid },
                                { "deletedAt", resolvedDeletedAtObj },
                                { "title", title },
                                { "description", description },
                                { "status", itemStatus },
                                { "price", price },
                                { "soldPrice", ParseIntSafe(item["soldPrice"], 0) },
                                { "profit", profit },
                                { "statusLabel", statusLabel },
                                { "isShared", true },
                                { "isDeleted", isTrashed },
                                { "syncedToPhone", true },
                                { "createdAt", resolvedCreatedAt },
                                { "updatedAt", resolvedUpdatedAt },
                                { "soldAt", resolvedSoldAt },
                                { "photos", savedPhotosList },
                                { "mergedParentId", resolvedMergedParentIdObj },
                                { "mergedParentSyncUuid", string.IsNullOrEmpty(parentSyncUuid) ? null : (object)parentSyncUuid },
                                { "mergedChildCount", item["mergedChildCount"] },
                                { "bundleLevel", item["bundleLevel"] },
                                { "mergedChildTitles", item["mergedChildTitles"] },
                                { "originalTotalBundlePrice", item["originalTotalBundlePrice"] },
                                { "discountAmount", item["discountAmount"] },
                                { "shippingCost", ParseIntSafe(item["shippingCost"], 210) },
                                { "feeRatePercent", ParseIntSafe(item["feeRatePercent"], 5) },
                                { "expenseCost", ParseIntSafe(item["expenseCost"], 55) },
                                { "useCustomSettings", ParseBoolSafe(item["useCustomSettings"], false) }
                            };

                            if (existingItem != null)
                            {
                                itemsDb.Remove(existingItem);
                            }
                            itemsDb.Add(newItemDict);
                            dataChanged = true;
                        }

                        // プロトコルv3以前の互換動作は廃止（無条件のディレクトリ物理削除は一切行わない）
                        ScanAndIndexLocalPhotos();
                    }
                }

                if (dataChanged)
                {
                    BumpDataRevision();
                    SaveItemsDbToDisk();
                }

                JObject dsObj = json["defaultSettings"] as JObject;
                if (dsObj != null)
                {
                    try
                    {
                        var incomingCfg = ParseDefaultSettingsConfig(dsObj);
                        var currentCfg = SyncStore.GetDefaultSettings();
                        if (incomingCfg.settingsUpdatedAt > currentCfg.settingsUpdatedAt)
                        {
                            SyncStore.SaveDefaultSettings(incomingCfg);
                            BumpSettingsRevision();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("基本設定の同期保存例外: " + ex.Message);
                    }
                }

                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "protocolVersion", 4 },
                    { "storeId", SyncStore.StoreId },
                    { "syncedHashes", syncedHashes },
                    { "neededHashes", neededHashes },
                    { "permanentlyDeletedUuids", SyncStore.GetPermanentlyDeletedUuids() },
                    { "dataRevision", GetDataRevision() },
                    { "settingsRevision", GetSettingsRevision() }
                });
                return;
            }
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
                                foreach (var p in SyncStore.NormalizePhotosList(item["photos"]))
                                {
                                    if (p != null && p.ContainsKey("md5Hash") && p["md5Hash"] != null &&
                                        string.Equals(p["md5Hash"].ToString().Trim(), computedHash, StringComparison.OrdinalIgnoreCase))
                                    {
                                        int id = Convert.ToInt32(item["id"]);
                                        string title = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "";
                                        targetDirPath = ResolveOrCreateItemFolder(id, title);
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    string photoFileName = "photo_" + computedHash.Substring(0, 8) + DetectImageExtension(photoBytes);
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
                int indexedCount;
                int missingCount;
                lock (lockObj)
                {
                    indexedCount = hashToFilePathMap.Count;
                    missingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
                }
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

            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);

                string title = json.Value<string>("title") ?? "";
                int price = Math.Max(0, ParseIntSafe(json["price"], 0));
                int soldPrice = Math.Max(0, ParseIntSafe(json["soldPrice"], 0));
                string status = json.Value<string>("status") ?? "LISTED";
                string description = json.Value<string>("description") ?? "";

                string statusLabel = GetStatusLabel(status);

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
                    bool useCustom = item.ContainsKey("useCustomSettings") && ParseBoolSafe(item["useCustomSettings"], false);

                    if (json["shippingCost"] != null && json["shippingCost"].Type != JTokenType.Null) shipping = Math.Max(0, ParseIntSafe(json["shippingCost"], shipping));
                    if (json["feeRatePercent"] != null && json["feeRatePercent"].Type != JTokenType.Null) feeRate = Math.Max(0, ParseIntSafe(json["feeRatePercent"], feeRate));
                    if (json["expenseCost"] != null && json["expenseCost"].Type != JTokenType.Null) expense = Math.Max(0, ParseIntSafe(json["expenseCost"], expense));
                    if (json["useCustomSettings"] != null && json["useCustomSettings"].Type != JTokenType.Null) useCustom = ParseBoolSafe(json["useCustomSettings"], useCustom);

                    int calcPrice = (IsSoldFamily(status) && soldPrice > 0) ? soldPrice : price;
                    int profit = Math.Max(0, calcPrice - (calcPrice * feeRate / 100) - shipping - expense);

                    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    long existingSoldAt = item.ContainsKey("soldAt") ? ParseLongSafe(item["soldAt"], 0) : 0;

                    item["title"] = title;
                    item["price"] = price;
                    // レビュー §30.4-3: 売却系でない場合は売却価格を 0 に正規化する (他経路と統一)
                    item["soldPrice"] = IsSoldFamily(status) ? soldPrice : 0;
                    item["status"] = status;
                    item["statusLabel"] = statusLabel;
                    item["description"] = description;
                    item["shippingCost"] = shipping;
                    item["feeRatePercent"] = feeRate;
                    item["expenseCost"] = expense;
                    item["useCustomSettings"] = useCustom;
                    item["profit"] = profit;
                    if (!item.ContainsKey("createdAt") || ParseLongSafe(item["createdAt"], 0) <= 0)
                    {
                        item["createdAt"] = nowMs;
                    }
                    item["updatedAt"] = nowMs;
                    item["soldAt"] = IsSoldFamily(status) ? (object)(existingSoldAt > 0 ? existingSoldAt : nowMs) : null;
                    item["syncedToPhone"] = false;
                    EnsureItemSyncUuid(item);

                    // フェーズ 6-4 / レビュー §30.4-1: まとめ親の状態変更に子の表示名を連動させる
                    // (従来は一括更新にしか無く、単体編集では子の表示名が古いまま残っていた)
                    int updChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (updChildCount > 0)
                    {
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                child["statusLabel"] = GetMergedChildStatusLabel(status);
                                child["updatedAt"] = nowMs;
                                child["syncedToPhone"] = false;
                            }
                        }
                    }

                    try
                    {
                        string targetDir = ResolveOrCreateItemFolder(itemId, title);
                        string txtPath = Path.Combine(targetDir, "商品情報_説明文.txt");
                        string txtContent = "【商品ID】 " + itemId + "\n【タイトル】 " + title + "\n【販売価格】 ¥" + price.ToString("N0") + "\n【純利益】 ¥" + profit.ToString("N0") + "\n【ステータス】 " + statusLabel + "\n\n--- 商品説明 ---\n" + description;
                        File.WriteAllText(txtPath, txtContent, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("説明文txt更新例外: " + ex.Message);
                    }
                }

                BumpDataRevision();
                SaveItemsDbToDisk();

                SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "dataRevision", GetDataRevision() } });
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
        string idStr = path.Substring("/api/items/".Length).Trim();
        MoveItemToTrashByIdStr(res, idStr);
    }

    private static void HandleTrashItem(HttpListenerResponse res, string path)
    {
        string idStr = path.Substring("/api/items/".Length);
        idStr = idStr.Substring(0, idStr.Length - "/trash".Length).Trim();
        MoveItemToTrashByIdStr(res, idStr);
    }

    private static void MoveItemToTrashByIdStr(HttpListenerResponse res, string idStr)
    {
        try
        {
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            bool found = false;
            lock (lockObj)
            {
                var item = itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == itemId);
                if (item != null)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    EnsureItemSyncUuid(item);
                    item["deletedAt"] = now;
                    item["isDeleted"] = true;
                    item["statusLabel"] = "ゴミ箱";
                    item["updatedAt"] = now;
                    item["syncedToPhone"] = false;

                    int mChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (mChildCount > 0)
                    {
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                EnsureItemSyncUuid(child);
                                child["deletedAt"] = now;
                                child["isDeleted"] = true;
                                child["updatedAt"] = now;
                                child["syncedToPhone"] = false;
                            }
                        }
                    }
                    found = true;
                }
            }

            if (!found)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Item not found" } });
                return;
            }

            BumpDataRevision();
            SaveItemsDbToDisk();
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "dataRevision", GetDataRevision() } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("TrashItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleRestoreItem(HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length);
            idStr = idStr.Substring(0, idStr.Length - "/restore".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            bool found = false;
            lock (lockObj)
            {
                var item = itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == itemId);
                if (item != null)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string st = item.ContainsKey("status") && item["status"] != null ? item["status"].ToString() : "LISTED";
                    EnsureItemSyncUuid(item);
                    item["deletedAt"] = null;
                    item["isDeleted"] = false;
                    string restoredLabel = GetStatusLabel(st);
                    if (st == "MERGED")
                    {
                        int pid = item.ContainsKey("mergedParentId") ? ParseIntSafe(item["mergedParentId"], 0) : 0;
                        var parentItem = pid > 0 ? itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == pid) : null;
                        string pst = parentItem != null && parentItem.ContainsKey("status") && parentItem["status"] != null ? parentItem["status"].ToString() : "LISTED";
                        restoredLabel = GetMergedChildStatusLabel(pst);
                    }
                    item["statusLabel"] = restoredLabel;
                    item["updatedAt"] = now;
                    item["syncedToPhone"] = false;

                    int mChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (mChildCount > 0)
                    {
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                EnsureItemSyncUuid(child);
                                child["deletedAt"] = null;
                                child["isDeleted"] = false;
                                child["statusLabel"] = GetMergedChildStatusLabel(st);
                                child["updatedAt"] = now;
                                child["syncedToPhone"] = false;
                            }
                        }
                    }
                    found = true;
                }
            }

            if (!found)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Item not found" } });
                return;
            }

            BumpDataRevision();
            SaveItemsDbToDisk();
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "dataRevision", GetDataRevision() } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("RestoreItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandlePermanentDeleteItem(HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length);
            idStr = idStr.Substring(0, idStr.Length - "/permanent".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            bool found = false;
            lock (lockObj)
            {
                var item = itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == itemId);
                if (item != null)
                {
                    var idsToDelete = new HashSet<int>();
                    var uuidsToRecord = new List<string>();
                    idsToDelete.Add(itemId);
                    string pUuid = EnsureItemSyncUuid(item);
                    if (!string.IsNullOrEmpty(pUuid)) uuidsToRecord.Add(pUuid);

                    int mChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (mChildCount > 0)
                    {
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                int cid = ParseIntSafe(child["id"], 0);
                                if (cid > 0) idsToDelete.Add(cid);
                                string cu = EnsureItemSyncUuid(child);
                                if (!string.IsNullOrEmpty(cu) && !uuidsToRecord.Contains(cu)) uuidsToRecord.Add(cu);
                            }
                        }
                    }

                    foreach (int did in idsToDelete)
                    {
                        DeleteItemFolderById(did);
                    }
                    SyncStore.RecordPermanentlyDeletedUuids(uuidsToRecord);
                    itemsDb.RemoveAll(i => i != null && i.ContainsKey("id") && idsToDelete.Contains(ParseIntSafe(i["id"], 0)));
                    found = true;
                }
            }

            if (!found)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Item not found" } });
                return;
            }

            ScanAndIndexLocalPhotos();
            BumpDataRevision();
            SaveItemsDbToDisk();
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "dataRevision", GetDataRevision() } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("PermanentDeleteItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static List<int> ParseIdListFromRequest(HttpListenerRequest req)
    {
        var ids = new List<int>();
        using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
        {
            string jsonStr = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(jsonStr)) return ids;
            JObject json = JObject.Parse(jsonStr);
            JArray arr = json["itemIds"] as JArray;
            if (arr != null)
            {
                foreach (JToken tok in arr)
                {
                    int val = ParseIntSafe(tok, 0);
                    if (val > 0 && !ids.Contains(val)) ids.Add(val);
                }
            }
        }
        return ids;
    }

    private static void HandleBatchTrashItems(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            List<int> targetIds = ParseIdListFromRequest(req);
            int affected = 0;
            lock (lockObj)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (int itemId in targetIds)
                {
                    var item = itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == itemId);
                    if (item == null) continue;
                    EnsureItemSyncUuid(item);
                    item["deletedAt"] = now;
                    item["isDeleted"] = true;
                    item["statusLabel"] = "ゴミ箱";
                    item["updatedAt"] = now;
                    item["syncedToPhone"] = false;
                    affected++;

                    int mChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (mChildCount > 0)
                    {
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                EnsureItemSyncUuid(child);
                                child["deletedAt"] = now;
                                child["isDeleted"] = true;
                                child["updatedAt"] = now;
                                child["syncedToPhone"] = false;
                            }
                        }
                    }
                }
            }

            if (affected > 0)
            {
                BumpDataRevision();
                SaveItemsDbToDisk();
            }
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "affectedCount", affected }, { "dataRevision", GetDataRevision() } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("BatchTrashItems エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleBatchRestoreItems(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            List<int> targetIds = ParseIdListFromRequest(req);
            int affected = 0;
            lock (lockObj)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (int itemId in targetIds)
                {
                    var item = itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == itemId);
                    if (item == null) continue;
                    string st = item.ContainsKey("status") && item["status"] != null ? item["status"].ToString() : "LISTED";
                    EnsureItemSyncUuid(item);
                    item["deletedAt"] = null;
                    item["isDeleted"] = false;
                    if (st == "MERGED")
                    {
                        // §31.3: 単体復元と揃え、子自身が対象の場合も親の状態から表示名を決める
                        int pid = item.ContainsKey("mergedParentId") ? ParseIntSafe(item["mergedParentId"], 0) : 0;
                        var parentItem = pid > 0 ? itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == pid) : null;
                        string pst = parentItem != null && parentItem.ContainsKey("status") && parentItem["status"] != null ? parentItem["status"].ToString() : "LISTED";
                        item["statusLabel"] = GetMergedChildStatusLabel(pst);
                    }
                    else
                    {
                        item["statusLabel"] = GetStatusLabel(st);
                    }
                    item["updatedAt"] = now;
                    item["syncedToPhone"] = false;
                    affected++;

                    int mChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (mChildCount > 0)
                    {
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                EnsureItemSyncUuid(child);
                                child["deletedAt"] = null;
                                child["isDeleted"] = false;
                                child["statusLabel"] = GetMergedChildStatusLabel(st);
                                child["updatedAt"] = now;
                                child["syncedToPhone"] = false;
                            }
                        }
                    }
                }
            }

            if (affected > 0)
            {
                BumpDataRevision();
                SaveItemsDbToDisk();
            }
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "affectedCount", affected }, { "dataRevision", GetDataRevision() } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("BatchRestoreItems エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleBatchPermanentDeleteItems(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            List<int> targetIds = ParseIdListFromRequest(req);
            int affected = 0;
            lock (lockObj)
            {
                var idsToDelete = new HashSet<int>();
                var uuidsToRecord = new List<string>();
                foreach (int itemId in targetIds)
                {
                    var item = itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == itemId);
                    if (item == null) continue;
                    idsToDelete.Add(itemId);
                    affected++;
                    string pUuid = EnsureItemSyncUuid(item);
                    if (!string.IsNullOrEmpty(pUuid) && !uuidsToRecord.Contains(pUuid)) uuidsToRecord.Add(pUuid);

                    int mChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (mChildCount > 0)
                    {
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                int cid = ParseIntSafe(child["id"], 0);
                                if (cid > 0) idsToDelete.Add(cid);
                                string cu = EnsureItemSyncUuid(child);
                                if (!string.IsNullOrEmpty(cu) && !uuidsToRecord.Contains(cu)) uuidsToRecord.Add(cu);
                            }
                        }
                    }
                }

                if (idsToDelete.Count > 0)
                {
                    foreach (int did in idsToDelete)
                    {
                        DeleteItemFolderById(did);
                    }
                    SyncStore.RecordPermanentlyDeletedUuids(uuidsToRecord);
                    itemsDb.RemoveAll(i => i != null && i.ContainsKey("id") && idsToDelete.Contains(ParseIntSafe(i["id"], 0)));
                }
            }

            if (affected > 0)
            {
                ScanAndIndexLocalPhotos();
                BumpDataRevision();
                SaveItemsDbToDisk();
            }
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "affectedCount", affected }, { "dataRevision", GetDataRevision() } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("BatchPermanentDeleteItems エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleBatchUpdateItems(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            JObject json = JObject.Parse(jsonStr);

            var targetIds = new List<int>();
            JArray arr = json["itemIds"] as JArray;
            if (arr != null)
            {
                foreach (JToken tok in arr)
                {
                    int val = ParseIntSafe(tok, 0);
                    if (val > 0 && !targetIds.Contains(val)) targetIds.Add(val);
                }
            }

            string newStatus = json.Value<string>("newStatus");
            if (string.IsNullOrWhiteSpace(newStatus)) newStatus = null;

            bool applyCostPreset = ParseBoolSafe(json["applyCostPreset"], false);
            bool useCustomSettings = ParseBoolSafe(json["useCustomSettings"], false);
            int feeRatePercent = Math.Max(0, ParseIntSafe(json["feeRatePercent"], 5));
            int shippingCost = Math.Max(0, ParseIntSafe(json["shippingCost"], 210));
            int expenseCost = Math.Max(0, ParseIntSafe(json["expenseCost"], 55));

            string priceMode = json.Value<string>("priceMode") ?? "NONE";
            int priceValue = ParseIntSafe(json["priceValue"], 0);

            int affected = 0;
            lock (lockObj)
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (int itemId in targetIds)
                {
                    var item = itemsDb.Find(i => i != null && ParseIntSafe(i["id"], 0) == itemId);
                    if (item == null) continue;
                    if (ParseBoolSafe(item.ContainsKey("isDeleted") ? item["isDeleted"] : null, false)) continue;
                    string curStatus = item.ContainsKey("status") && item["status"] != null ? item["status"].ToString() : "LISTED";
                    if (curStatus == "MERGED") continue;

                    int curPrice = Math.Max(0, ParseIntSafe(item.ContainsKey("price") ? item["price"] : null, 0));
                    int nextPrice = curPrice;
                    if (priceMode == "FIXED")
                    {
                        nextPrice = Math.Max(0, priceValue);
                    }
                    else if (priceMode == "DELTA_YEN")
                    {
                        nextPrice = Math.Max(0, curPrice + priceValue);
                    }
                    else if (priceMode == "DELTA_PERCENT")
                    {
                        nextPrice = Math.Max(0, curPrice + (curPrice * priceValue) / 100);
                    }

                    string nextStatus = newStatus != null ? newStatus : curStatus;
                    string nextStatusLabel = GetStatusLabel(nextStatus);

                    long existingSoldAt = item.ContainsKey("soldAt") && item["soldAt"] != null ? ParseLongSafe(item["soldAt"], 0) : 0;
                    int existingSoldPrice = item.ContainsKey("soldPrice") && item["soldPrice"] != null ? Math.Max(0, ParseIntSafe(item["soldPrice"], 0)) : 0;

                    int nextSoldPrice = 0;
                    if (IsSoldFamily(nextStatus))
                    {
                        if (priceMode != "NONE") nextSoldPrice = nextPrice;
                        else if (existingSoldPrice > 0) nextSoldPrice = existingSoldPrice;
                        else nextSoldPrice = nextPrice;
                    }

                    int nextShipping = applyCostPreset
                        ? shippingCost
                        : (item.ContainsKey("shippingCost") && item["shippingCost"] != null ? ParseIntSafe(item["shippingCost"], 210) : 210);
                    int nextFeeRate = applyCostPreset
                        ? feeRatePercent
                        : (item.ContainsKey("feeRatePercent") && item["feeRatePercent"] != null ? ParseIntSafe(item["feeRatePercent"], 5) : 5);
                    int nextExpense = applyCostPreset
                        ? expenseCost
                        : (item.ContainsKey("expenseCost") && item["expenseCost"] != null ? ParseIntSafe(item["expenseCost"], 55) : 55);
                    bool nextUseCustom = applyCostPreset
                        ? useCustomSettings
                        : (item.ContainsKey("useCustomSettings") && ParseBoolSafe(item["useCustomSettings"], false));

                    int baseForProfit = (IsSoldFamily(nextStatus) && nextSoldPrice > 0) ? nextSoldPrice : nextPrice;
                    int fee = (baseForProfit * nextFeeRate) / 100;
                    int profit = Math.Max(0, baseForProfit - fee - nextShipping - nextExpense);

                    item["status"] = nextStatus;
                    item["statusLabel"] = nextStatusLabel;
                    item["price"] = nextPrice;
                    item["soldPrice"] = nextSoldPrice;
                    item["soldAt"] = IsSoldFamily(nextStatus) ? (object)(existingSoldAt > 0 ? existingSoldAt : nowMs) : null;
                    item["useCustomSettings"] = nextUseCustom;
                    item["shippingCost"] = nextShipping;
                    item["feeRatePercent"] = nextFeeRate;
                    item["expenseCost"] = nextExpense;
                    item["profit"] = profit;
                    item["updatedAt"] = nowMs;
                    item["syncedToPhone"] = false;
                    EnsureItemSyncUuid(item);

                    int mChildCount = item.ContainsKey("mergedChildCount") ? ParseIntSafe(item["mergedChildCount"], 0) : 0;
                    if (mChildCount > 0 && newStatus != null)
                    {
                        string childLabel = GetMergedChildStatusLabel(nextStatus);
                        foreach (var child in itemsDb)
                        {
                            if (child != null && child.ContainsKey("mergedParentId") && ParseIntSafe(child["mergedParentId"], 0) == itemId)
                            {
                                child["statusLabel"] = childLabel;
                                child["updatedAt"] = nowMs;
                                child["syncedToPhone"] = false;
                            }
                        }
                    }

                    try
                    {
                        string title = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "";
                        string description = item.ContainsKey("description") && item["description"] != null ? item["description"].ToString() : "";
                        string targetDir = ResolveOrCreateItemFolder(itemId, title);
                        string txtPath = Path.Combine(targetDir, "商品情報_説明文.txt");
                        string txtContent = "【商品ID】 " + itemId + "\r\n" +
                            "【タイトル】 " + title + "\r\n" +
                            "【販売価格】 ¥" + nextPrice.ToString("N0") + "\r\n" +
                            "【純利益】 ¥" + profit.ToString("N0") + "\r\n" +
                            "【ステータス】 " + nextStatusLabel + "\r\n\r\n" +
                            "--- 商品説明 ---\r\n" + description;
                        File.WriteAllText(txtPath, txtContent, Encoding.UTF8);
                    }
                    catch (Exception tex)
                    {
                        Console.WriteLine("一括更新 説明文txt例外: " + tex.Message);
                    }

                    affected++;
                }
            }

            if (affected > 0)
            {
                BumpDataRevision();
                SaveItemsDbToDisk();
            }
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "affectedCount", affected }, { "dataRevision", GetDataRevision() } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("BatchUpdateItems エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleEmptyTrash(HttpListenerResponse res)
    {
        try
        {
            int deletedCount = 0;
            lock (lockObj)
            {
                var idsToDelete = new HashSet<int>();
                var uuidsToRecord = new List<string>();
                foreach (var item in itemsDb)
                {
                    if (item == null || !IsItemTrashed(item)) continue;
                    int id = ParseIntSafe(item["id"], 0);
                    if (id > 0) idsToDelete.Add(id);
                    string u = EnsureItemSyncUuid(item);
                    if (!string.IsNullOrEmpty(u) && !uuidsToRecord.Contains(u)) uuidsToRecord.Add(u);
                }

                // ゴミ箱親商品の子商品も含める
                if (idsToDelete.Count > 0)
                {
                    foreach (var item in itemsDb)
                    {
                        if (item == null) continue;
                        int mPid = item.ContainsKey("mergedParentId") ? ParseIntSafe(item["mergedParentId"], 0) : 0;
                        if (mPid > 0 && idsToDelete.Contains(mPid))
                        {
                            int cid = ParseIntSafe(item["id"], 0);
                            if (cid > 0) idsToDelete.Add(cid);
                            string cu = EnsureItemSyncUuid(item);
                            if (!string.IsNullOrEmpty(cu) && !uuidsToRecord.Contains(cu)) uuidsToRecord.Add(cu);
                        }
                    }

                    foreach (int did in idsToDelete)
                    {
                        DeleteItemFolderById(did);
                    }
                    SyncStore.RecordPermanentlyDeletedUuids(uuidsToRecord);
                    itemsDb.RemoveAll(i => i != null && i.ContainsKey("id") && idsToDelete.Contains(ParseIntSafe(i["id"], 0)));
                    deletedCount = idsToDelete.Count;
                }
            }

            if (deletedCount > 0)
            {
                ScanAndIndexLocalPhotos();
                BumpDataRevision();
                SaveItemsDbToDisk();
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "deletedCount", deletedCount },
                { "dataRevision", GetDataRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("EmptyTrash エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleCreateItem(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);

                string title = json.Value<string>("title") ?? "新規商品";
                int price = Math.Max(0, ParseIntSafe(json["price"], 0));
                int soldPrice = json["soldPrice"] != null && json["soldPrice"].Type != JTokenType.Null
                    ? Math.Max(0, ParseIntSafe(json["soldPrice"], 0))
                    : 0;
                string status = json.Value<string>("status") ?? "DRAFT";
                string description = json.Value<string>("description") ?? "";
                bool useCustomSettings = ParseBoolSafe(json["useCustomSettings"], false);

                var defaults = SyncStore.GetDefaultSettings();
                int shippingCost = json["shippingCost"] != null && json["shippingCost"].Type != JTokenType.Null
                    ? Math.Max(0, ParseIntSafe(json["shippingCost"], defaults.defaultShippingCost))
                    : defaults.defaultShippingCost;
                int feeRatePercent = json["feeRatePercent"] != null && json["feeRatePercent"].Type != JTokenType.Null
                    ? Math.Max(0, ParseIntSafe(json["feeRatePercent"], defaults.defaultFeeRatePercent))
                    : defaults.defaultFeeRatePercent;
                int expenseCost = json["expenseCost"] != null && json["expenseCost"].Type != JTokenType.Null
                    ? Math.Max(0, ParseIntSafe(json["expenseCost"], defaults.defaultExpenseCost))
                    : defaults.defaultExpenseCost;

                int calcPrice = (IsSoldFamily(status) && soldPrice > 0) ? soldPrice : price;
                int profit = Math.Max(0, calcPrice - (calcPrice * feeRatePercent / 100) - shippingCost - expenseCost);
                string statusLabel = GetStatusLabel(status);
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                int newId = 1;
                string newUuid = Guid.NewGuid().ToString();
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
                        { "syncUuid", newUuid },
                        { "deletedAt", null },
                        { "title", title },
                        { "description", description },
                        { "status", status },
                        { "statusLabel", statusLabel },
                        { "price", price },
                        { "soldPrice", IsSoldFamily(status) ? soldPrice : 0 },
                        { "profit", profit },
                        { "shippingCost", shippingCost },
                        { "feeRatePercent", feeRatePercent },
                        { "expenseCost", expenseCost },
                        { "useCustomSettings", useCustomSettings },
                        { "isShared", true },
                        { "isDeleted", false },
                        { "syncedToPhone", false },
                        { "createdAt", now },
                        { "updatedAt", now },
                        { "soldAt", IsSoldFamily(status) ? (object)now : null },
                        { "photos", new List<Dictionary<string, object>>() }
                    };

                    itemsDb.Add(newItemDict);
                }

                BumpDataRevision();
                SaveItemsDbToDisk();

                SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "id", newId }, { "syncUuid", newUuid }, { "dataRevision", GetDataRevision() } });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("CreateItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static DefaultSettingsConfig ParseDefaultSettingsConfig(JObject dsObj)
    {
        var cfg = new DefaultSettingsConfig
        {
            defaultFeeRatePercent = Math.Max(0, ParseIntSafe(dsObj["defaultFeeRatePercent"], 5)),
            defaultShippingCost = Math.Max(0, ParseIntSafe(dsObj["defaultShippingCost"], 210)),
            defaultExpenseCost = Math.Max(0, ParseIntSafe(dsObj["defaultExpenseCost"], 55)),
            activePresetId = dsObj.Value<string>("activePresetId") ?? "yahoo_furima",
            presets = new List<FeePresetConfig>(),
            titleTemplates = new List<TextTemplateConfig>(),
            descriptionTemplates = new List<TextTemplateConfig>(),
            promptTemplates = new List<TextTemplateConfig>(),
            settingsUpdatedAt = Math.Max(0, ParseLongSafe(dsObj["settingsUpdatedAt"], 0))
        };

        JArray presetsArr = dsObj["presets"] as JArray;
        if (presetsArr != null)
        {
            foreach (JObject pToken in presetsArr)
            {
                if (pToken == null) continue;
                cfg.presets.Add(new FeePresetConfig
                {
                    id = pToken.Value<string>("id") ?? "",
                    name = pToken.Value<string>("name") ?? "",
                    feeRatePercent = Math.Max(0, ParseIntSafe(pToken["feeRatePercent"], 5)),
                    shippingCost = Math.Max(0, ParseIntSafe(pToken["shippingCost"], 210)),
                    expenseCost = Math.Max(0, ParseIntSafe(pToken["expenseCost"], 55))
                });
            }
        }
        if (cfg.presets.Count == 0)
        {
            cfg.presets = DefaultSettingsConfig.CreateDefault().presets;
        }

        JArray titleTplArr = dsObj["titleTemplates"] as JArray;
        if (titleTplArr != null)
        {
            foreach (JObject tToken in titleTplArr)
            {
                if (tToken == null) continue;
                cfg.titleTemplates.Add(new TextTemplateConfig
                {
                    id = tToken.Value<string>("id") ?? "",
                    name = tToken.Value<string>("name") ?? "",
                    content = tToken.Value<string>("content") ?? ""
                });
            }
        }

        JArray descTplArr = dsObj["descriptionTemplates"] as JArray;
        if (descTplArr != null)
        {
            foreach (JObject tToken in descTplArr)
            {
                if (tToken == null) continue;
                cfg.descriptionTemplates.Add(new TextTemplateConfig
                {
                    id = tToken.Value<string>("id") ?? "",
                    name = tToken.Value<string>("name") ?? "",
                    content = tToken.Value<string>("content") ?? ""
                });
            }
        }

        JArray promptTplArr = dsObj["promptTemplates"] as JArray;
        if (promptTplArr != null)
        {
            foreach (JObject tToken in promptTplArr)
            {
                if (tToken == null) continue;
                cfg.promptTemplates.Add(new TextTemplateConfig
                {
                    id = tToken.Value<string>("id") ?? "",
                    name = tToken.Value<string>("name") ?? "",
                    content = tToken.Value<string>("content") ?? ""
                });
            }
        }

        return cfg;
    }

    private static void HandleUpdateSettings(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string jsonStr = reader.ReadToEnd();
                JObject json = JObject.Parse(jsonStr);
                var cfg = ParseDefaultSettingsConfig(json);
                cfg.settingsUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SyncStore.SaveDefaultSettings(cfg);
                BumpSettingsRevision();
                SendJson(res, SyncStore.GetDefaultSettings());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("UpdateSettings エラー: " + ex.Message);
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

    private static long ParseLongSafe(object val, long defaultVal = 0)
    {
        if (val == null) return defaultVal;
        var jv = val as Newtonsoft.Json.Linq.JValue;
        if (jv != null && jv.Type == Newtonsoft.Json.Linq.JTokenType.Null) return defaultVal;
        try { return Convert.ToInt64(val); } catch { return defaultVal; }
    }

    private static bool ParseBoolSafe(object val, bool defaultVal = false)
    {
        if (val == null) return defaultVal;
        var jv = val as Newtonsoft.Json.Linq.JValue;
        if (jv != null && jv.Type == Newtonsoft.Json.Linq.JTokenType.Null) return defaultVal;
        try { return Convert.ToBoolean(val); } catch { return defaultVal; }
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

                var mergeDefaults = SyncStore.GetDefaultSettings();
                string mergedTitle = json.Value<string>("mergedTitle") ?? "まとめ買い商品";
                string mergedDescription = json.Value<string>("mergedDescription") ?? "";
                int totalSoldPrice = Math.Max(0, ParseIntSafe(json["totalSoldPrice"], 0));
                int newShippingCost = json["newShippingCost"] != null && json["newShippingCost"].Type != JTokenType.Null
                    ? Math.Max(0, ParseIntSafe(json["newShippingCost"], mergeDefaults.defaultShippingCost))
                    : mergeDefaults.defaultShippingCost;
                int newFeeRatePercent = json["newFeeRatePercent"] != null && json["newFeeRatePercent"].Type != JTokenType.Null
                    ? Math.Max(0, ParseIntSafe(json["newFeeRatePercent"], mergeDefaults.defaultFeeRatePercent))
                    : mergeDefaults.defaultFeeRatePercent;
                int newExpenseCost = json["newExpenseCost"] != null && json["newExpenseCost"].Type != JTokenType.Null
                    ? Math.Max(0, ParseIntSafe(json["newExpenseCost"], mergeDefaults.defaultExpenseCost))
                    : mergeDefaults.defaultExpenseCost;
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

                    // フェーズ 6-4 / レビュー §30.4-1: 子商品の前提をサーバー側でも検証する
                    // (従来は画面側のチェックのみだった)。まとめに入れられるのは下書き系と出品中だけ。
                    foreach (var child in childItems)
                    {
                        string cStatus = child.ContainsKey("status") && child["status"] != null ? child["status"].ToString() : "LISTED";
                        if (IsItemTrashed(child) || (cStatus != "DRAFT" && cStatus != "DRAFT_DONE" && cStatus != "LISTED"))
                        {
                            res.StatusCode = 400;
                            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "まとめ買いに含められない商品があります (ID: " + ParseIntSafe(child["id"], 0) + ")" } });
                            return;
                        }
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
                        child["statusLabel"] = GetMergedChildStatusLabel(asStatus);
                        child["mergedParentId"] = newParentId;
                        child["updatedAt"] = now;
                        child["syncedToPhone"] = false;
                    }

                    int profit = totalSoldPrice - newShippingCost - (totalSoldPrice * newFeeRatePercent / 100) - newExpenseCost;

                    int totalChildCount = 0;
                    foreach (var child in childItems)
                    {
                        int cCount = child.ContainsKey("mergedChildCount") ? ParseIntSafe(child["mergedChildCount"], 0) : 0;
                        totalChildCount += (cCount > 0) ? cCount : 1;
                    }

                    string parentUuid = Guid.NewGuid().ToString();
                    var parentDict = new Dictionary<string, object>
                    {
                        { "id", newParentId },
                        { "syncUuid", parentUuid },
                        { "deletedAt", null },
                        { "title", mergedTitle },
                        { "description", mergedDescription },
                        { "status", asStatus },
                        { "statusLabel", GetStatusLabel(asStatus) },
                        { "price", totalSoldPrice },
                        { "soldPrice", IsSoldFamily(asStatus) ? totalSoldPrice : 0 },
                        { "profit", profit },
                        { "shippingCost", newShippingCost },
                        { "feeRatePercent", newFeeRatePercent },
                        { "expenseCost", newExpenseCost },
                        { "useCustomSettings", true },
                        { "mergedChildCount", totalChildCount },
                        { "mergedChildTitles", string.Join(", ", childTitlesList.ToArray()) },
                        { "originalTotalBundlePrice", originalTotal },
                        { "discountAmount", Math.Max(0, originalTotal - totalSoldPrice) },
                        { "bundleLevel", maxChildLevel + 1 },
                        { "createdAt", now },
                        { "updatedAt", now },
                        { "soldAt", IsSoldFamily(asStatus) ? (object)now : null },
                        { "isShared", true },
                        { "isDeleted", false },
                        { "syncedToPhone", false },
                        { "photos", parentPhotos }
                    };

                    foreach (var child in childItems)
                    {
                        EnsureItemSyncUuid(child);
                        child["mergedParentSyncUuid"] = parentUuid;
                    }

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

                BumpDataRevision();
                SaveItemsDbToDisk();

                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "parentId", newParentId },
                    { "dataRevision", GetDataRevision() },
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
                            EnsureItemSyncUuid(item);
                            item["status"] = "LISTED";
                            item["statusLabel"] = "出品中";
                            item["mergedParentId"] = null;
                            item["mergedParentSyncUuid"] = null;
                            item["deletedAt"] = null;
                            item["isDeleted"] = false;
                            item["updatedAt"] = now;
                            item["syncedToPhone"] = false;
                            restoredCount++;
                        }
                    }
                }

                // フォールバック: 写真内の childItemId や MERGED ステータスからの救済復元
                if (restoredCount == 0 && parentItem.ContainsKey("photos") && parentItem["photos"] != null)
                {
                    var childIdsFromPhotos = new HashSet<int>();
                    foreach (var pDict in SyncStore.NormalizePhotosList(parentItem["photos"]))
                    {
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
                            EnsureItemSyncUuid(cItem);
                            cItem["status"] = "LISTED";
                            cItem["statusLabel"] = "出品中";
                            cItem["mergedParentId"] = null;
                            cItem["mergedParentSyncUuid"] = null;
                            cItem["deletedAt"] = null;
                            cItem["isDeleted"] = false;
                            cItem["updatedAt"] = now;
                            cItem["syncedToPhone"] = false;
                            restoredCount++;
                        }
                    }
                }

                // 親商品は物理除去＋完全削除UUID記録＋親フォルダ削除（スマホへも完全削除伝播しゾンビ復活防止）
                string parentUuid = EnsureItemSyncUuid(parentItem);
                if (!string.IsNullOrEmpty(parentUuid))
                {
                    SyncStore.RecordPermanentlyDeletedUuid(parentUuid);
                }
                DeleteItemFolderById(parentId);
                itemsDb.Remove(parentItem);
            }

            ScanAndIndexLocalPhotos();
            BumpDataRevision();
            SaveItemsDbToDisk();

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "restoredCount", restoredCount },
                { "dataRevision", GetDataRevision() },
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

                string title = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "";
                string itemFolder = ResolveOrCreateItemFolder(itemId, title);

                string photoFileName = "photo_" + computedHash.Substring(0, 8) + DetectImageExtension(photoBytes);
                string photoFilePath = Path.Combine(itemFolder, photoFileName);
                if (!File.Exists(photoFilePath))
                {
                    File.WriteAllBytes(photoFilePath, photoBytes);
                }

                hashToFilePathMap[computedHash] = photoFilePath;

                var photosList = item.ContainsKey("photos") && item["photos"] != null
                    ? SyncStore.NormalizePhotosList(item["photos"])
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
                        { "photoUrl", "/api/photos/" + computedHash },
                        { "md5Hash", computedHash },
                        { "displayOrder", photosList.Count }
                    });
                    item["photos"] = photosList;
                    item["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    item["syncedToPhone"] = false;
                    BumpDataRevision();
                }
            }

            SaveItemsDbToDisk();

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "hash", computedHash },
                { "dataRevision", GetDataRevision() },
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

    private class PhotoTrashTarget
    {
        public int itemId;
        public string md5Hash;
        public int childItemId;
    }

    private static bool IsHashReferencedByAnyItemLocked(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        foreach (var item in itemsDb)
        {
            if (item == null || !item.ContainsKey("photos") || item["photos"] == null) continue;
            var list = SyncStore.NormalizePhotosList(item["photos"]);
            foreach (var p in list)
            {
                if (p == null || !p.ContainsKey("md5Hash") || p["md5Hash"] == null) continue;
                string h = p["md5Hash"].ToString().Trim();
                if (string.Equals(h, hash, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static int TrashPhotosFromTargets(List<PhotoTrashTarget> targets, out int remainingCountFirstItem)
    {
        remainingCountFirstItem = 0;
        if (targets == null || targets.Count == 0) return 0;

        var newTrashEntries = new List<PhotoTrashEntry>();
        var touchedHashes = new List<string>();
        var pendingTrashMeta = new List<PhotoTrashEntry>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool anyRemoved = false;

        lock (lockObj)
        {
            for (int tIdx = 0; tIdx < targets.Count; tIdx++)
            {
                var target = targets[tIdx];
                if (target == null || target.itemId <= 0 || string.IsNullOrEmpty(target.md5Hash)) continue;
                string cleanHash = target.md5Hash.Trim().ToLower();

                var item = itemsDb.Find(i => ParseIntSafe(i["id"]) == target.itemId);
                if (item == null) continue;

                EnsureItemSyncUuid(item);
                string itemUuid = item.ContainsKey("syncUuid") && item["syncUuid"] != null ? item["syncUuid"].ToString() : "";
                string itemTitle = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "無題";

                var currentPhotos = item.ContainsKey("photos") && item["photos"] != null
                    ? SyncStore.NormalizePhotosList(item["photos"])
                    : new List<Dictionary<string, object>>();

                var updatedPhotos = new List<Dictionary<string, object>>();
                bool removedForTarget = false;
                int removedDisplayOrder = 0;
                int removedChildId = target.childItemId;
                string removedChildTitle = "";

                foreach (var dictCopy in currentPhotos)
                {
                    if (dictCopy == null) continue;
                    string h = dictCopy.ContainsKey("md5Hash") && dictCopy["md5Hash"] != null
                        ? dictCopy["md5Hash"].ToString().Trim().ToLower()
                        : "";
                    int pChildId = dictCopy.ContainsKey("childItemId") && dictCopy["childItemId"] != null
                        ? ParseIntSafe(dictCopy["childItemId"], 0)
                        : 0;

                    bool hashMatches = string.Equals(h, cleanHash, StringComparison.OrdinalIgnoreCase);
                    bool childMatches = (target.childItemId <= 0) || (pChildId == 0) || (pChildId == target.childItemId);

                    if (!removedForTarget && hashMatches && childMatches)
                    {
                        removedForTarget = true;
                        anyRemoved = true;
                        removedDisplayOrder = dictCopy.ContainsKey("displayOrder") ? ParseIntSafe(dictCopy["displayOrder"], updatedPhotos.Count) : updatedPhotos.Count;
                        if (pChildId > 0) removedChildId = pChildId;
                        if (dictCopy.ContainsKey("childItemTitle") && dictCopy["childItemTitle"] != null)
                        {
                            removedChildTitle = dictCopy["childItemTitle"].ToString();
                        }
                        continue;
                    }

                    dictCopy["displayOrder"] = updatedPhotos.Count;
                    updatedPhotos.Add(dictCopy);
                }

                if (tIdx == 0)
                {
                    remainingCountFirstItem = updatedPhotos.Count;
                }

                if (removedForTarget)
                {
                    item["photos"] = updatedPhotos;
                    item["updatedAt"] = now;
                    item["syncedToPhone"] = false;

                    // まとめ親商品のギャラリーから子商品の写真を削除した場合、子商品側の写真リストからも除外して整合性を保つ
                    if (removedChildId > 0 && removedChildId != target.itemId)
                    {
                        var childItem = itemsDb.Find(c => ParseIntSafe(c["id"]) == removedChildId);
                        if (childItem != null && childItem.ContainsKey("photos") && childItem["photos"] != null)
                        {
                            if (string.IsNullOrEmpty(removedChildTitle) && childItem.ContainsKey("title") && childItem["title"] != null)
                            {
                                removedChildTitle = childItem["title"].ToString();
                            }
                            var childPhotos = SyncStore.NormalizePhotosList(childItem["photos"]);
                            var updatedChildPhotos = new List<Dictionary<string, object>>();
                            bool childRemoved = false;
                            foreach (var cp in childPhotos)
                            {
                                if (cp == null) continue;
                                string ch = cp.ContainsKey("md5Hash") && cp["md5Hash"] != null
                                    ? cp["md5Hash"].ToString().Trim().ToLower()
                                    : "";
                                if (!childRemoved && string.Equals(ch, cleanHash, StringComparison.OrdinalIgnoreCase))
                                {
                                    childRemoved = true;
                                    continue;
                                }
                                cp["displayOrder"] = updatedChildPhotos.Count;
                                updatedChildPhotos.Add(cp);
                            }
                            if (childRemoved)
                            {
                                childItem["photos"] = updatedChildPhotos;
                                childItem["updatedAt"] = now;
                                childItem["syncedToPhone"] = false;
                            }
                        }
                    }

                    touchedHashes.Add(cleanHash);
                    pendingTrashMeta.Add(new PhotoTrashEntry
                    {
                        trashId = Guid.NewGuid().ToString("N"),
                        originalItemId = target.itemId,
                        originalItemUuid = itemUuid,
                        originalItemTitle = itemTitle,
                        originalChildItemId = removedChildId,
                        originalChildItemTitle = removedChildTitle,
                        md5Hash = cleanHash,
                        fileName = "photo_" + (cleanHash.Length >= 8 ? cleanHash.Substring(0, 8) : cleanHash) + ".jpg",
                        originalFilePath = "",
                        trashFilePath = "",
                        displayOrder = removedDisplayOrder,
                        trashedAt = now,
                        fileSizeBytes = 0
                    });
                }
            }

            if (anyRemoved)
            {
                string trashDir = SyncStore.GetPhotoTrashDir();
                if (!string.IsNullOrEmpty(trashDir) && !Directory.Exists(trashDir))
                {
                    Directory.CreateDirectory(trashDir);
                }

                for (int i = 0; i < pendingTrashMeta.Count; i++)
                {
                    var entry = pendingTrashMeta[i];
                    string sourcePath = "";
                    if (hashToFilePathMap.ContainsKey(entry.md5Hash))
                    {
                        sourcePath = hashToFilePathMap[entry.md5Hash];
                    }

                    if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath) && !string.IsNullOrEmpty(trashDir))
                    {
                        entry.originalFilePath = sourcePath;
                        entry.fileName = Path.GetFileName(sourcePath);
                        string destTrashPath = Path.Combine(trashDir, entry.trashId + "_" + entry.fileName);
                        bool stillReferenced = IsHashReferencedByAnyItemLocked(entry.md5Hash);
                        try
                        {
                            FileInfo fi = new FileInfo(sourcePath);
                            entry.fileSizeBytes = fi.Length;
                            if (!stillReferenced)
                            {
                                try
                                {
                                    File.Move(sourcePath, destTrashPath);
                                }
                                catch
                                {
                                    File.Copy(sourcePath, destTrashPath, true);
                                    File.Delete(sourcePath);
                                }
                                hashToFilePathMap.Remove(entry.md5Hash);
                            }
                            else
                            {
                                File.Copy(sourcePath, destTrashPath, true);
                            }
                            entry.trashFilePath = destTrashPath;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[警告] 写真ゴミ箱退避失敗: " + ex.Message);
                        }
                    }

                    newTrashEntries.Add(entry);
                }

                BumpDataRevision();
            }
        }

        if (anyRemoved)
        {
            if (newTrashEntries.Count > 0)
            {
                SyncStore.AddPhotoTrashEntries(newTrashEntries);
            }
            SaveItemsDbToDisk();
            if (qrForm != null)
            {
                qrForm.UpdateStatusInfo(hashToFilePathMap.Count, SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap));
            }
        }

        return newTrashEntries.Count;
    }

    private static void HandleDeletePhoto(HttpListenerRequest req, HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length);
            idStr = idStr.Substring(0, idStr.Length - "/photos/delete".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            string targetHash = "";
            int childItemId = 0;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string jsonStr = reader.ReadToEnd();
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    JObject json = JObject.Parse(jsonStr);
                    targetHash = (json.Value<string>("md5Hash") ?? "").Trim().ToLower();
                    if (json["childItemId"] != null && json["childItemId"].Type != JTokenType.Null)
                    {
                        childItemId = ParseIntSafe(json["childItemId"], 0);
                    }
                }
            }

            if (string.IsNullOrEmpty(targetHash))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "md5Hash が指定されていません" } });
                return;
            }

            int remainingCount;
            int movedCount = TrashPhotosFromTargets(
                new List<PhotoTrashTarget> { new PhotoTrashTarget { itemId = itemId, md5Hash = targetHash, childItemId = childItemId } },
                out remainingCount
            );

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "removed", movedCount > 0 },
                { "movedToTrashCount", movedCount },
                { "remainingCount", remainingCount },
                { "dataRevision", GetDataRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleDeletePhoto エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleTrashPhotosBatch(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            if (string.IsNullOrEmpty(jsonStr))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Empty body" } });
                return;
            }

            JObject json = JObject.Parse(jsonStr);
            JArray targetsArr = json["targets"] as JArray;
            var targets = new List<PhotoTrashTarget>();
            if (targetsArr != null)
            {
                foreach (var tok in targetsArr)
                {
                    var obj = tok as JObject;
                    if (obj == null) continue;
                    int tid = ParseIntSafe(obj["itemId"], 0);
                    string hash = (obj.Value<string>("md5Hash") ?? "").Trim().ToLower();
                    int cid = obj["childItemId"] != null && obj["childItemId"].Type != JTokenType.Null
                        ? ParseIntSafe(obj["childItemId"], 0)
                        : 0;
                    if (tid > 0 && !string.IsNullOrEmpty(hash))
                    {
                        targets.Add(new PhotoTrashTarget { itemId = tid, md5Hash = hash, childItemId = cid });
                    }
                }
            }

            if (targets.Count == 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "移動対象の写真が指定されていません" } });
                return;
            }

            int dummyRemaining;
            int movedCount = TrashPhotosFromTargets(targets, out dummyRemaining);

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "movedToTrashCount", movedCount },
                { "trashCount", SyncStore.GetPhotoTrashEntries().Count },
                { "dataRevision", GetDataRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleTrashPhotosBatch エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleGetPhotoTrash(HttpListenerResponse res)
    {
        try
        {
            var entries = SyncStore.GetPhotoTrashEntries();
            long totalBytes = 0;
            var items = new List<Dictionary<string, object>>();
            foreach (var e in entries)
            {
                long sz = e.fileSizeBytes;
                if (sz <= 0 && !string.IsNullOrEmpty(e.trashFilePath) && File.Exists(e.trashFilePath))
                {
                    try { sz = new FileInfo(e.trashFilePath).Length; } catch { }
                }
                totalBytes += sz;
                items.Add(new Dictionary<string, object>
                {
                    { "trashId", e.trashId },
                    { "originalItemId", e.originalItemId },
                    { "originalItemUuid", e.originalItemUuid ?? "" },
                    { "originalItemTitle", e.originalItemTitle ?? "無題" },
                    { "originalChildItemId", e.originalChildItemId },
                    { "originalChildItemTitle", e.originalChildItemTitle ?? "" },
                    { "md5Hash", e.md5Hash ?? "" },
                    { "fileName", e.fileName ?? "" },
                    { "trashedAt", e.trashedAt },
                    { "fileSizeBytes", sz },
                    { "hasImageFile", !string.IsNullOrEmpty(e.trashFilePath) && File.Exists(e.trashFilePath) },
                    { "imageUrl", "/api/photo-trash/" + e.trashId + "/image" }
                });
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "count", items.Count },
                { "totalSizeBytes", totalBytes },
                { "entries", items }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleGetPhotoTrash エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void ServePhotoTrashImage(HttpListenerResponse res, string trashId)
    {
        try
        {
            var entry = SyncStore.GetPhotoTrashEntry(trashId);
            if (entry != null && !string.IsNullOrEmpty(entry.trashFilePath) && File.Exists(entry.trashFilePath))
            {
                byte[] bytes = File.ReadAllBytes(entry.trashFilePath);
                string trashContentType = DetectImageMimeType(bytes);
                res.ContentType = string.IsNullOrEmpty(trashContentType) ? "application/octet-stream" : trashContentType;
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes, 0, bytes.Length);
                res.OutputStream.Close();
                return;
            }
            if (entry != null && !string.IsNullOrEmpty(entry.md5Hash))
            {
                ServePhotoByHash(res, entry.md5Hash);
                return;
            }
        }
        catch { }
        res.StatusCode = 404;
        res.Close();
    }

    private static void HandleRestorePhotoTrash(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            JObject json = string.IsNullOrEmpty(jsonStr) ? new JObject() : JObject.Parse(jsonStr);
            JArray idsArr = json["trashIds"] as JArray;
            var requestedIds = new List<string>();
            if (idsArr != null)
            {
                foreach (var tok in idsArr)
                {
                    if (tok != null && tok.Type != JTokenType.Null)
                    {
                        string id = tok.ToString().Trim();
                        if (!string.IsNullOrEmpty(id)) requestedIds.Add(id);
                    }
                }
            }

            if (requestedIds.Count == 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "復元する写真IDが指定されていません" } });
                return;
            }

            var restoredIds = new List<string>();
            var skippedReasons = new List<string>();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (lockObj)
            {
                foreach (string tid in requestedIds)
                {
                    var entry = SyncStore.GetPhotoTrashEntry(tid);
                    if (entry == null)
                    {
                        skippedReasons.Add("エントリが見つかりません (" + tid + ")");
                        continue;
                    }

                    Dictionary<string, object> targetItem = null;
                    if (!string.IsNullOrEmpty(entry.originalItemUuid))
                    {
                        targetItem = itemsDb.Find(i =>
                            i.ContainsKey("syncUuid") && i["syncUuid"] != null &&
                            string.Equals(i["syncUuid"].ToString(), entry.originalItemUuid, StringComparison.OrdinalIgnoreCase));
                    }
                    if (targetItem == null && entry.originalItemId > 0)
                    {
                        targetItem = itemsDb.Find(i => ParseIntSafe(i["id"]) == entry.originalItemId);
                    }

                    if (targetItem == null)
                    {
                        skippedReasons.Add("元の商品が見つかりません (" + (entry.originalItemTitle ?? "無題") + ")");
                        continue;
                    }

                    bool isItemTrashed = (targetItem.ContainsKey("deletedAt") && targetItem["deletedAt"] != null) ||
                                         (targetItem.ContainsKey("isDeleted") && targetItem["isDeleted"] is bool && (bool)targetItem["isDeleted"]);
                    if (isItemTrashed)
                    {
                        skippedReasons.Add("元の商品が商品ゴミ箱に移動されているため復元できません (" + (entry.originalItemTitle ?? "無題") + ")");
                        continue;
                    }

                    int targetItemId = ParseIntSafe(targetItem["id"], entry.originalItemId);
                    string targetTitle = targetItem.ContainsKey("title") && targetItem["title"] != null ? targetItem["title"].ToString() : "商品";

                    if (!string.IsNullOrEmpty(entry.trashFilePath) && File.Exists(entry.trashFilePath))
                    {
                        string itemFolder = ResolveOrCreateItemFolder(targetItemId, targetTitle);
                        string safeFileName = string.IsNullOrEmpty(entry.fileName)
                            ? ("photo_" + entry.md5Hash.Substring(0, Math.Min(8, entry.md5Hash.Length)) + ".jpg")
                            : entry.fileName;
                        string destPath = Path.Combine(itemFolder, safeFileName);
                        if (File.Exists(destPath))
                        {
                            string baseName = Path.GetFileNameWithoutExtension(safeFileName);
                            string ext = Path.GetExtension(safeFileName);
                            destPath = Path.Combine(itemFolder, baseName + "_restored_" + entry.trashId.Substring(0, 6) + ext);
                        }
                        try
                        {
                            File.Move(entry.trashFilePath, destPath);
                        }
                        catch
                        {
                            File.Copy(entry.trashFilePath, destPath, true);
                            try { File.Delete(entry.trashFilePath); } catch { }
                        }
                        if (!string.IsNullOrEmpty(entry.md5Hash))
                        {
                            hashToFilePathMap[entry.md5Hash.Trim().ToLower()] = destPath;
                        }
                    }

                    var photosList = targetItem.ContainsKey("photos") && targetItem["photos"] != null
                        ? SyncStore.NormalizePhotosList(targetItem["photos"])
                        : new List<Dictionary<string, object>>();

                    bool alreadyInList = false;
                    foreach (var p in photosList)
                    {
                        string h = p.ContainsKey("md5Hash") && p["md5Hash"] != null ? p["md5Hash"].ToString().Trim().ToLower() : "";
                        int pCid = p.ContainsKey("childItemId") && p["childItemId"] != null ? ParseIntSafe(p["childItemId"], 0) : 0;
                        if (string.Equals(h, entry.md5Hash, StringComparison.OrdinalIgnoreCase) && pCid == entry.originalChildItemId)
                        {
                            alreadyInList = true;
                            break;
                        }
                    }

                    if (!alreadyInList)
                    {
                        var photoDict = new Dictionary<string, object>
                        {
                            { "id", 0 },
                            { "itemId", targetItemId },
                            { "photoUrl", "/api/photos/" + entry.md5Hash },
                            { "md5Hash", entry.md5Hash },
                            { "displayOrder", photosList.Count }
                        };
                        if (entry.originalChildItemId > 0)
                        {
                            photoDict["childItemId"] = entry.originalChildItemId;
                            if (!string.IsNullOrEmpty(entry.originalChildItemTitle))
                            {
                                photoDict["childItemTitle"] = entry.originalChildItemTitle;
                            }
                        }
                        photosList.Add(photoDict);
                        targetItem["photos"] = photosList;
                    }

                    targetItem["updatedAt"] = now;
                    targetItem["syncedToPhone"] = false;

                    // まとめ子商品側にも復元連動
                    if (entry.originalChildItemId > 0 && entry.originalChildItemId != targetItemId)
                    {
                        var childItem = itemsDb.Find(c => ParseIntSafe(c["id"]) == entry.originalChildItemId);
                        if (childItem != null)
                        {
                            var childPhotos = childItem.ContainsKey("photos") && childItem["photos"] != null
                                ? SyncStore.NormalizePhotosList(childItem["photos"])
                                : new List<Dictionary<string, object>>();
                            bool childHas = false;
                            foreach (var cp in childPhotos)
                            {
                                string ch = cp.ContainsKey("md5Hash") && cp["md5Hash"] != null ? cp["md5Hash"].ToString().Trim().ToLower() : "";
                                if (string.Equals(ch, entry.md5Hash, StringComparison.OrdinalIgnoreCase))
                                {
                                    childHas = true;
                                    break;
                                }
                            }
                            if (!childHas)
                            {
                                childPhotos.Add(new Dictionary<string, object>
                                {
                                    { "id", 0 },
                                    { "itemId", entry.originalChildItemId },
                                    { "photoUrl", "/api/photos/" + entry.md5Hash },
                                    { "md5Hash", entry.md5Hash },
                                    { "displayOrder", childPhotos.Count }
                                });
                                childItem["photos"] = childPhotos;
                            }
                            childItem["updatedAt"] = now;
                            childItem["syncedToPhone"] = false;
                        }
                    }

                    restoredIds.Add(tid);
                }

                if (restoredIds.Count > 0)
                {
                    BumpDataRevision();
                }
            }

            if (restoredIds.Count > 0)
            {
                SyncStore.RemovePhotoTrashEntries(restoredIds);
                SaveItemsDbToDisk();
                if (qrForm != null)
                {
                    qrForm.UpdateStatusInfo(hashToFilePathMap.Count, SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap));
                }
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", restoredIds.Count > 0 ? "ok" : "error" },
                { "restoredCount", restoredIds.Count },
                { "skippedReasons", skippedReasons },
                { "trashCount", SyncStore.GetPhotoTrashEntries().Count },
                { "dataRevision", GetDataRevision() },
                { "message", restoredIds.Count > 0 ? (restoredIds.Count + "枚の写真を元の商品へ復元しました") : (skippedReasons.Count > 0 ? skippedReasons[0] : "復元に失敗しました") }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleRestorePhotoTrash エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void DeletePhotoTrashFileQuietly(PhotoTrashEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.trashFilePath)) return;
        try
        {
            if (File.Exists(entry.trashFilePath))
            {
                File.Delete(entry.trashFilePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] _photo_trash ファイル物理削除失敗: " + ex.Message);
        }
    }

    private static void HandleDeletePhotoTrashPermanent(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            JObject json = string.IsNullOrEmpty(jsonStr) ? new JObject() : JObject.Parse(jsonStr);
            JArray idsArr = json["trashIds"] as JArray;
            var targetIds = new List<string>();
            if (idsArr != null)
            {
                foreach (var tok in idsArr)
                {
                    if (tok != null && tok.Type != JTokenType.Null)
                    {
                        string id = tok.ToString().Trim();
                        if (!string.IsNullOrEmpty(id)) targetIds.Add(id);
                    }
                }
            }

            var removed = SyncStore.RemovePhotoTrashEntries(targetIds);
            foreach (var entry in removed)
            {
                DeletePhotoTrashFileQuietly(entry);
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "deletedCount", removed.Count },
                { "trashCount", SyncStore.GetPhotoTrashEntries().Count }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleDeletePhotoTrashPermanent エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleEmptyPhotoTrash(HttpListenerResponse res)
    {
        try
        {
            var removed = SyncStore.ClearAllPhotoTrashEntries();
            foreach (var entry in removed)
            {
                DeletePhotoTrashFileQuietly(entry);
            }
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "deletedCount", removed.Count },
                { "trashCount", 0 }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleEmptyPhotoTrash エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    // ===== 「Museに送る」ジョブAPI (フェーズ 5-2) =====

    private static Dictionary<string, object> ToPublicClaudeJob(ClaudeJob job)
    {
        // jobToken は一覧・詳細のいずれの応答にも絶対に含めない (計画書 0.6 / レビュー指摘8-3)。
        // トークンの発行は 5-4 で行い、開示範囲はその時点で別途定める。
        var photos = new List<Dictionary<string, object>>();
        if (job.photos != null)
        {
            foreach (var ph in job.photos)
            {
                photos.Add(new Dictionary<string, object>
                {
                    { "md5", ph.md5 ?? "" },
                    { "fileName", ph.fileName ?? "" }
                });
            }
        }
        var results = new List<Dictionary<string, object>>();
        if (job.results != null)
        {
            foreach (var r in job.results)
            {
                results.Add(new Dictionary<string, object>
                {
                    { "sourceMd5", r.sourceMd5 ?? "" },
                    { "resultFileName", r.resultFileName ?? "" },
                    { "resultMd5", r.resultMd5 ?? "" }
                });
            }
        }
        var jobErrors = new List<Dictionary<string, object>>();
        if (job.errors != null)
        {
            foreach (var e in job.errors)
            {
                jobErrors.Add(new Dictionary<string, object>
                {
                    { "sourceMd5", e.sourceMd5 ?? "" },
                    { "message", e.message ?? "" }
                });
            }
        }
        return new Dictionary<string, object>
        {
            { "jobId", job.jobId ?? "" },
            { "itemId", job.itemId },
            { "itemUuid", job.itemUuid ?? "" },
            { "itemTitle", job.itemTitle ?? "" },
            { "folderName", job.folderName ?? "" },
            { "photos", photos },
            { "templateId", job.templateId ?? "" },
            { "templateName", job.templateName ?? "" },
            { "promptText", job.promptText ?? "" },
            { "origin", job.origin ?? "" },
            { "queue", string.IsNullOrEmpty(job.queue) ? "manual" : job.queue },
            { "status", job.status ?? "" },
            { "createdAt", job.createdAt },
            { "approvalRequestedAt", job.approvalRequestedAt },
            { "results", results },
            { "errors", jobErrors },
            { "completedAt", job.completedAt },
            { "acknowledged", job.acknowledged },
            { "acknowledgedAt", job.acknowledgedAt },
            { "errorDismissed", job.errorDismissed }
        };
    }

    private static void HandleCreateClaudeJob(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            if (string.IsNullOrEmpty(jsonStr))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Empty body" } });
                return;
            }

            JObject json = JObject.Parse(jsonStr);
            int itemId = ParseIntSafe(json["itemId"], 0);
            string itemUuid = (json.Value<string>("itemUuid") ?? "").Trim();
            string templateId = (json.Value<string>("templateId") ?? "").Trim();
            string origin = (json.Value<string>("origin") ?? "").Trim();
            if (origin != "pc" && origin != "phone") origin = "pc";
            string queue = (json.Value<string>("queue") ?? "").Trim();
            if (queue != "patrol" && queue != "manual" && queue != "waiting") queue = "patrol";

            var reqPhotos = new List<ClaudeJobPhoto>();
            JArray photosArr = json["photos"] as JArray;
            if (photosArr != null)
            {
                foreach (var tok in photosArr)
                {
                    var obj = tok as JObject;
                    if (obj == null) continue;
                    string md5 = (obj.Value<string>("md5") ?? "").Trim().ToLower();
                    string fileName = (obj.Value<string>("fileName") ?? "").Trim();
                    if (!string.IsNullOrEmpty(md5))
                    {
                        reqPhotos.Add(new ClaudeJobPhoto { md5 = md5, fileName = fileName });
                    }
                }
            }

            if (reqPhotos.Count == 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "送信する写真が指定されていません" } });
                return;
            }
            if (string.IsNullOrEmpty(templateId))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "プロンプトテンプレートが指定されていません" } });
                return;
            }

            ClaudeJob created = null;
            string errorMessage = null;
            int errorCode = 400;
            lock (lockObj)
            {
                Dictionary<string, object> item = null;
                if (!string.IsNullOrEmpty(itemUuid))
                {
                    // 商品特定は itemUuid (syncUuid) を最優先する (Q1 / レビュー指摘1)
                    item = itemsDb.Find(i => i != null && i.ContainsKey("syncUuid") && i["syncUuid"] != null &&
                        string.Equals(i["syncUuid"].ToString().Trim(), itemUuid, StringComparison.OrdinalIgnoreCase));
                    if (item == null) { errorCode = 404; errorMessage = "商品が見つかりません (itemUuid)"; }
                }
                else if (itemId > 0)
                {
                    item = itemsDb.Find(i => i != null && i.ContainsKey("id") && ParseIntSafe(i["id"], 0) == itemId);
                    if (item == null) { errorCode = 404; errorMessage = "商品が見つかりません"; }
                }
                else
                {
                    errorMessage = "商品が指定されていません";
                }

                if (errorMessage == null && item != null)
                {
                    int resolvedItemId = ParseIntSafe(item["id"], 0);
                    if (IsItemTrashed(item))
                    {
                        errorMessage = "ゴミ箱内の商品は送信できません";
                    }
                    else if (ParseIntSafe(item.ContainsKey("mergedChildCount") ? item["mergedChildCount"] : null, 0) > 0)
                    {
                        // まとめ親商品からの送信は受け付けない (Q5 / レビュー指摘6)
                        errorMessage = "まとめ商品は個別の子商品から送信してください";
                    }
                    else
                    {
                        TextTemplateConfig tpl = null;
                        var settings = SyncStore.GetDefaultSettings();
                        if (settings != null && settings.promptTemplates != null)
                        {
                            tpl = settings.promptTemplates.Find(t => t != null && t.id == templateId);
                        }
                        if (tpl == null)
                        {
                            errorMessage = "プロンプトテンプレートが見つかりません";
                        }
                        else
                        {
                            var photosList = item.ContainsKey("photos") && item["photos"] != null
                                ? SyncStore.NormalizePhotosList(item["photos"])
                                : new List<Dictionary<string, object>>();
                            var md5ToFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var ph in photosList)
                            {
                                if (ph == null) continue;
                                string h = ph.ContainsKey("md5Hash") && ph["md5Hash"] != null ? ph["md5Hash"].ToString().Trim().ToLower() : "";
                                if (string.IsNullOrEmpty(h) || md5ToFileName.ContainsKey(h)) continue;
                                string fn = ph.ContainsKey("fileName") && ph["fileName"] != null ? ph["fileName"].ToString().Trim() : "";
                                md5ToFileName[h] = fn;
                            }

                            var jobPhotos = new List<ClaudeJobPhoto>();
                            string missingMd5 = null;
                            foreach (var rp in reqPhotos)
                            {
                                if (!md5ToFileName.ContainsKey(rp.md5)) { missingMd5 = rp.md5; break; }
                                string fn = rp.fileName;
                                if (string.IsNullOrEmpty(fn)) fn = md5ToFileName[rp.md5];
                                if (string.IsNullOrEmpty(fn) && hashToFilePathMap.ContainsKey(rp.md5))
                                {
                                    fn = Path.GetFileName(hashToFilePathMap[rp.md5]);
                                }
                                jobPhotos.Add(new ClaudeJobPhoto { md5 = rp.md5, fileName = fn ?? "" });
                            }

                            if (missingMd5 != null)
                            {
                                errorMessage = "商品に存在しない写真が含まれています: " + missingMd5;
                            }
                            else
                            {
                                string folderName = "";
                                string dir = FindExistingItemFolder(resolvedItemId);
                                if (!string.IsNullOrEmpty(dir)) folderName = Path.GetFileName(dir);
                                var job = new ClaudeJob
                                {
                                    itemId = resolvedItemId,
                                    itemUuid = item.ContainsKey("syncUuid") && item["syncUuid"] != null ? item["syncUuid"].ToString().Trim() : itemUuid,
                                    itemTitle = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "",
                                    folderName = folderName,
                                    photos = jobPhotos,
                                    templateId = tpl.id,
                                    templateName = tpl.name ?? "",
                                    promptText = tpl.content ?? "",
                                    origin = origin,
                                    queue = queue
                                };
                                created = ClaudeJobStore.CreateJob(job);
                            }
                        }
                    }
                }
            }

            if (errorMessage != null)
            {
                res.StatusCode = errorCode;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", errorMessage } });
                return;
            }

            // リビジョン加算は CreateJob が単一ロック内で行う (フェーズ 6-1a)。
            // ここで二重に加算しないこと。
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", ToPublicClaudeJob(created) },
                { "claudeRevision", GetClaudeRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleCreateClaudeJob エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleGetClaudeJobs(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string statusFilter = null;
            string query = req.Url != null ? (req.Url.Query ?? "") : "";
            if (!string.IsNullOrEmpty(query))
            {
                foreach (var pair in query.TrimStart('?').Split('&'))
                {
                    var kv = pair.Split(new char[] { '=' }, 2);
                    if (kv.Length == 2 && kv[0] == "status")
                    {
                        statusFilter = Uri.UnescapeDataString(kv[1]).Trim();
                    }
                }
            }
            var jobs = ClaudeJobStore.GetJobs(statusFilter);
            foreach (var listedJob in jobs)
            {
                // 失効の遅延評価 (Q7): 承認待ち10分超過は queued へ、トークン期限切れは approval_expired へ
                ApplyApprovalPendingExpiry(listedJob);
                ApplyTokenExpiry(listedJob);
            }
            jobs = ClaudeJobStore.GetJobs(statusFilter);
            var list = new List<Dictionary<string, object>>();
            foreach (var job in jobs)
            {
                list.Add(ToPublicClaudeJob(job));
            }
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "jobs", list },
                { "claudeRevision", GetClaudeRevision() },
                { "labelSeenAtByItemUuid", ClaudeJobStore.GetLabelSeenAtSnapshot() },
                { "labelSeenRevision", ClaudeJobStore.GetLabelSeenRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleGetClaudeJobs エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleGetClaudeJob(HttpListenerResponse res, string jobId)
    {
        try
        {
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            ApplyApprovalPendingExpiry(job);
            ApplyTokenExpiry(job);
            job = ClaudeJobStore.GetJob(jobId);
            var pub = ToPublicClaudeJob(job);
            // jobToken は approved / processing の詳細応答でのみ開示する (5-4-1)。
            // 一覧・作成・取消の応答には絶対に含めない。
            if ((job.status == "approved" || job.status == "processing") && !string.IsNullOrEmpty(job.jobToken))
            {
                pub["jobToken"] = job.jobToken;
                pub["jobTokenExpiresAt"] = job.jobTokenExpiresAt;
            }
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", pub },
                { "claudeRevision", GetClaudeRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleGetClaudeJob エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    // 承認待ち (approval_pending) の10分失効を遅延評価する (Q7)。遷移したら true。
    private static bool ApplyApprovalPendingExpiry(ClaudeJob job)
    {
        if (job == null) return false;
        if (job.status != "approval_pending" || job.approvalRequestedAt <= 0) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - job.approvalRequestedAt <= 10L * 60L * 1000L) return false;
        bool changed = false;
        ClaudeJobStore.MutateJob(job.jobId, j =>
        {
            if (j.status == "approval_pending")
            {
                j.status = "queued";
                j.queue = "waiting";
                j.approvalRequestedAt = 0;
                changed = true;
                return true;
            }
            return false;
        });
        if (changed) BumpClaudeRevision();
        return changed;
    }

    // ジョブトークンの期限切れを遅延評価する (Q7)。全トークン対象APIと一覧・詳細の読み取りから呼ぶ。
    // フェーズ 6-1a: 遷移先は記録の有無で分ける (計画書 §10 の 7-3)。
    // 未着手 (results / errors がともに空) なら待機へ戻し、1件でも記録がある場合は
    // 未処理の写真へ失効エラーを記録して終端させる (同一ジョブの再実行は行わない)。
    private static bool ApplyTokenExpiry(ClaudeJob job)
    {
        if (job == null) return false;
        if (job.status != "approved" && job.status != "processing") return false;
        if (string.IsNullOrEmpty(job.jobToken) || job.jobTokenExpiresAt <= 0) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= job.jobTokenExpiresAt) return false;
        bool changed = false;
        ClaudeJobStore.MutateJob(job.jobId, j =>
        {
            if (j.status == "approved" || j.status == "processing")
            {
                bool untouched = (j.results == null || j.results.Count == 0) && (j.errors == null || j.errors.Count == 0);
                if (untouched)
                {
                    j.status = "queued";
                    j.queue = "waiting";
                }
                else
                {
                    if (j.results == null) j.results = new List<ClaudeJobResult>();
                    if (j.errors == null) j.errors = new List<ClaudeJobError>();
                    var settled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in j.results) { if (r != null && !string.IsNullOrEmpty(r.sourceMd5)) settled.Add(r.sourceMd5); }
                    foreach (var e in j.errors) { if (e != null && !string.IsNullOrEmpty(e.sourceMd5)) settled.Add(e.sourceMd5); }
                    if (j.photos != null)
                    {
                        foreach (var p in j.photos)
                        {
                            if (p == null || string.IsNullOrEmpty(p.md5)) continue;
                            if (!settled.Contains(p.md5))
                            {
                                j.errors.Add(new ClaudeJobError { sourceMd5 = p.md5, message = "トークンの有効期限が切れたため、未処理のまま終了しました" });
                            }
                        }
                    }
                    bool allResults = j.photos != null && j.photos.Count > 0;
                    if (allResults)
                    {
                        foreach (var p in j.photos)
                        {
                            bool hasResult = false;
                            foreach (var r in j.results)
                            {
                                if (r != null && string.Equals(r.sourceMd5, p.md5, StringComparison.OrdinalIgnoreCase)) { hasResult = true; break; }
                            }
                            if (!hasResult) { allResults = false; break; }
                        }
                    }
                    j.status = allResults ? "done" : "done_with_errors";
                    j.completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
                j.jobToken = null;
                j.jobTokenExpiresAt = 0;
                changed = true;
                return true;
            }
            return false;
        });
        if (changed) BumpClaudeRevision();
        return changed;
    }

    private static void HandleRequestClaudeApproval(HttpListenerResponse res, string jobId)
    {
        try
        {
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            ApplyApprovalPendingExpiry(job);
            ApplyTokenExpiry(job);
            job = ClaudeJobStore.GetJob(jobId);
            if (job.status != "queued" || job.queue == "waiting")
            {
                res.StatusCode = 400;
                string msg = job.queue == "waiting"
                    ? "待機中のジョブは、ジョブ一覧から巡回／手動へ移して承認リクエストしてください"
                    : "承認リクエストできる状態ではありません (status: " + job.status + ")";
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", msg } });
                return;
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ClaudeJobStore.MutateJob(jobId, j =>
            {
                j.status = "approval_pending";
                j.approvalRequestedAt = now;
                j.jobToken = null;
                j.jobTokenExpiresAt = 0;
                return true;
            });
            BumpClaudeRevision();
            job = ClaudeJobStore.GetJob(jobId);
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", ToPublicClaudeJob(job) },
                { "claudeRevision", GetClaudeRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleRequestClaudeApproval エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleClaudeApproval(HttpListenerRequest req, HttpListenerResponse res, string jobId)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            bool approved = false;
            if (!string.IsNullOrEmpty(jsonStr))
            {
                JObject json = JObject.Parse(jsonStr);
                var approvedToken = json["approved"];
                if (approvedToken != null && approvedToken.Type == JTokenType.Boolean)
                {
                    approved = approvedToken.Value<bool>();
                }
            }

            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            ApplyApprovalPendingExpiry(job);
            job = ClaudeJobStore.GetJob(jobId);
            if (job.status != "approval_pending")
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "承認待ち状態ではありません (status: " + job.status + ")" } });
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (approved)
            {
                // jobToken は権限分離ではなく「スマホ承認が完了しない限り実行APIが通らない」
                // セーフティロックとして発行する (レビュー指摘8-2)。有効期限30分・当該ジョブ専用。
                string newToken = Guid.NewGuid().ToString();
                long expiresAt = now + 30L * 60L * 1000L;
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    j.status = "approved";
                    j.jobToken = newToken;
                    j.jobTokenExpiresAt = expiresAt;
                    return true;
                });
            }
            else
            {
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    j.status = "denied";
                    j.completedAt = now;
                    j.jobToken = null;
                    j.jobTokenExpiresAt = 0;
                    return true;
                });
            }
            BumpClaudeRevision();
            job = ClaudeJobStore.GetJob(jobId);
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", ToPublicClaudeJob(job) },
                { "claudeRevision", GetClaudeRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleClaudeApproval エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void RecordClaudeJobPhotoError(string jobId, string md5, string message)
    {
        bool added = false;
        ClaudeJobStore.MutateJob(jobId, j =>
        {
            if (j.errors == null) j.errors = new List<ClaudeJobError>();
            foreach (var e in j.errors)
            {
                if (string.Equals(e.sourceMd5, md5, StringComparison.OrdinalIgnoreCase)) return false;
            }
            j.errors.Add(new ClaudeJobError { sourceMd5 = md5, message = message });
            // Build 13 (13-3): 実行が始まったジョブは取り消せないよう、初回エラー記録でも processing へ進める
            if (j.status == "approved") j.status = "processing";
            added = true;
            return true;
        });
        if (added) BumpClaudeRevision();
    }

    private static void HandleGetClaudeJobPhoto(HttpListenerRequest req, HttpListenerResponse res, string jobId, string md5)
    {
        try
        {
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            ApplyApprovalPendingExpiry(job);
            ApplyTokenExpiry(job);
            job = ClaudeJobStore.GetJob(jobId);

            string token = req.Headers["X-Claude-Job-Token"];
            if (string.IsNullOrEmpty(job.jobToken) || string.IsNullOrEmpty(token) ||
                !string.Equals(token, job.jobToken, StringComparison.Ordinal))
            {
                res.StatusCode = 403;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブトークンが無効です（未承認・期限切れ・不一致）" } });
                return;
            }
            if (job.status != "approved" && job.status != "processing")
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが実行可能な状態ではありません (status: " + job.status + ")" } });
                return;
            }

            ClaudeJobPhoto photo = null;
            if (job.photos != null)
            {
                photo = job.photos.Find(ph => string.Equals(ph.md5, md5, StringComparison.OrdinalIgnoreCase));
            }
            if (photo == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "写真がジョブに含まれていません" } });
                return;
            }

            string filePath = null;
            lock (lockObj)
            {
                if (hashToFilePathMap.ContainsKey(md5)) filePath = hashToFilePathMap[md5];
            }
            if ((string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) &&
                !string.IsNullOrEmpty(job.folderName) && !string.IsNullOrEmpty(photo.fileName))
            {
                string candidate = Path.Combine(syncDir, job.folderName, photo.fileName);
                if (File.Exists(candidate)) filePath = candidate;
            }
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                // Build 13 (13-3): エラーを記録しないと complete が永久に失敗するため、404 でも写真単位エラーとして残す
                RecordClaudeJobPhotoError(job.jobId, md5, "写真ファイルが見つかりません");
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "写真ファイルが見つかりません" } });
                return;
            }

            byte[] raw = File.ReadAllBytes(filePath);
            byte[] outBytes;
            string outMime;
            string copyError;
            if (!TryBuildClaudeDeliveryCopy(raw, out outBytes, out outMime, out copyError))
            {
                // 未対応形式などは写真単位のエラーとして記録し、他の写真は継続可能にする
                RecordClaudeJobPhotoError(job.jobId, md5, copyError);
                res.StatusCode = 415;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", copyError } });
                return;
            }

            if (job.status == "approved")
            {
                // 初回取得: 状態遷移と同じ変更でトークン期限も延長する (6-1b、二重保存の回避)
                ClaudeJobStore.MutateJob(job.jobId, j =>
                {
                    if (j.status == "approved")
                    {
                        j.status = "processing";
                        j.jobTokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30L * 60L * 1000L;
                        return true;
                    }
                    return false;
                });
                BumpClaudeRevision();
            }
            else
            {
                ExtendClaudeJobToken(job.jobId);
            }

            res.ContentType = outMime;
            res.ContentLength64 = outBytes.Length;
            res.OutputStream.Write(outBytes, 0, outBytes.Length);
            res.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleGetClaudeJobPhoto エラー: " + ex.Message);
            try
            {
                res.StatusCode = 500;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
            }
            catch { }
        }
    }

    // 配達用コピーの生成 (確定事項9)。原本は変更しない。変換はメモリ上で完結する。
    // パススルー条件: JPEG/PNG かつ長辺2048px以下 かつ 5MB以下 かつ EXIF Orientation が正立(1)または未設定。
    // それ以外は正立化＋必要に応じ長辺2048pxへ縮小し、JPEG品質90で再エンコードする。
    private static bool TryBuildClaudeDeliveryCopy(byte[] raw, out byte[] outBytes, out string outMime, out string error)
    {
        outBytes = null;
        outMime = null;
        error = null;
        string mime = DetectImageMimeType(raw);
        if (mime != "image/jpeg" && mime != "image/png")
        {
            error = "未対応の画像形式です (" + mime + ")";
            return false;
        }
        try
        {
            using (var ms = new MemoryStream(raw))
            using (var img = Image.FromStream(ms))
            {
                int orientation = 1;
                try
                {
                    if (Array.IndexOf(img.PropertyIdList, 0x0112) >= 0)
                    {
                        var prop = img.GetPropertyItem(0x0112);
                        if (prop != null && prop.Value != null && prop.Value.Length >= 2)
                        {
                            orientation = prop.Value[0] | (prop.Value[1] << 8);
                        }
                    }
                }
                catch { orientation = 1; }

                int longEdge = Math.Max(img.Width, img.Height);
                const long maxBytes = 5L * 1024L * 1024L;
                if (orientation == 1 && longEdge <= 2048 && raw.Length <= maxBytes)
                {
                    outBytes = raw;
                    outMime = mime;
                    return true;
                }

                if (orientation != 1)
                {
                    img.RotateFlip(MapExifOrientationToRotateFlip(orientation));
                    try { img.RemovePropertyItem(0x0112); } catch { }
                }
                int w = img.Width;
                int h = img.Height;
                int le = Math.Max(w, h);
                double scale = le > 2048 ? (double)2048 / le : 1.0;
                int tw = Math.Max(1, (int)Math.Round(w * scale));
                int th = Math.Max(1, (int)Math.Round(h * scale));
                using (var bmp = new Bitmap(tw, th))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(img, 0, 0, tw, th);
                    }
                    var jpegCodec = GetJpegEncoder();
                    if (jpegCodec == null)
                    {
                        error = "JPEGエンコーダが見つかりません";
                        return false;
                    }
                    using (var encParams = new EncoderParameters(1))
                    {
                        encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                        using (var outMs = new MemoryStream())
                        {
                            bmp.Save(outMs, jpegCodec, encParams);
                            outBytes = outMs.ToArray();
                        }
                    }
                }
                outMime = "image/jpeg";
                return true;
            }
        }
        catch (Exception ex)
        {
            error = "画像の変換に失敗しました: " + ex.Message;
            return false;
        }
    }

    private static RotateFlipType MapExifOrientationToRotateFlip(int orientation)
    {
        switch (orientation)
        {
            case 2: return RotateFlipType.RotateNoneFlipX;
            case 3: return RotateFlipType.Rotate180FlipNone;
            case 4: return RotateFlipType.Rotate180FlipX;
            case 5: return RotateFlipType.Rotate90FlipX;
            case 6: return RotateFlipType.Rotate90FlipNone;
            case 7: return RotateFlipType.Rotate270FlipX;
            case 8: return RotateFlipType.Rotate270FlipNone;
            default: return RotateFlipType.RotateNoneFlipNone;
        }
    }

    private static ImageCodecInfo GetJpegEncoder()
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
        }
        return null;
    }

    private static string SanitizeClaudeResultBaseName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return "";
        string name = Path.GetFileName(rawName);
        name = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrEmpty(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (Array.IndexOf(invalid, c) >= 0) continue;
            sb.Append(c);
        }
        string cleaned = sb.ToString().Trim();
        if (cleaned.StartsWith(".")) cleaned = cleaned.TrimStart('.');
        if (cleaned.Length > 80) cleaned = cleaned.Substring(0, 80);
        return cleaned;
    }

    private static bool ValidateClaudeJobToken(HttpListenerRequest req, HttpListenerResponse res, ClaudeJob job)
    {
        string token = req.Headers["X-Claude-Job-Token"];
        if (string.IsNullOrEmpty(job.jobToken) || string.IsNullOrEmpty(token) ||
            !string.Equals(token, job.jobToken, StringComparison.Ordinal))
        {
            res.StatusCode = 403;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブトークンが無効です（未承認・期限切れ・不一致）" } });
            return false;
        }
        return true;
    }

    // ジョブトークンのスライディング延長 (フェーズ 6-1b)。
    // 実行系エンドポイント (写真取得・結果返却・エラー報告) の検証を通過した後にのみ呼ぶ。
    // 失効評価 (ApplyTokenExpiry) を通過したトークンだけがここへ来るため、失効済みは復活しない。
    // BumpClaudeRevision は呼ばない (延長は読み取り側へ知らせる変化ではない)。
    private static void ExtendClaudeJobToken(string jobId)
    {
        long newExpiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30L * 60L * 1000L;
        ClaudeJobStore.MutateJob(jobId, j =>
        {
            if ((j.status == "approved" || j.status == "processing") && !string.IsNullOrEmpty(j.jobToken))
            {
                j.jobTokenExpiresAt = newExpiry;
                return true;
            }
            return false;
        });
    }

    private static void HandlePostClaudeJobResult(HttpListenerRequest req, HttpListenerResponse res, string jobId)
    {
        try
        {
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            ApplyTokenExpiry(job);
            job = ClaudeJobStore.GetJob(jobId);
            if (!ValidateClaudeJobToken(req, res, job)) return;
            if (job.status != "approved" && job.status != "processing")
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが実行可能な状態ではありません (status: " + job.status + ")" } });
                return;
            }

            string sourceMd5 = (req.Headers["X-Source-Md5"] ?? "").Trim();
            if (string.IsNullOrEmpty(sourceMd5))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "X-Source-Md5 ヘッダが必要です" } });
                return;
            }
            ClaudeJobPhoto srcPhoto = null;
            if (job.photos != null)
            {
                srcPhoto = job.photos.Find(ph => string.Equals(ph.md5, sourceMd5, StringComparison.OrdinalIgnoreCase));
            }
            if (srcPhoto == null)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "元写真がジョブに含まれていません" } });
                return;
            }

            const long maxResultBytes = 25L * 1024L * 1024L;
            if (req.ContentLength64 > maxResultBytes)
            {
                res.StatusCode = 413;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "結果画像が上限サイズ (25MB) を超えています" } });
                return;
            }
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                req.InputStream.CopyTo(ms);
                bytes = ms.ToArray();
            }
            if (bytes.Length == 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "結果画像のボディが空です" } });
                return;
            }
            if (bytes.Length > maxResultBytes)
            {
                res.StatusCode = 413;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "結果画像が上限サイズ (25MB) を超えています" } });
                return;
            }

            // ロック外でマジックバイト検証とMD5計算を済ませる (レビュー指摘5-A)
            string mime = DetectImageMimeType(bytes);
            if (string.IsNullOrEmpty(mime) || !mime.StartsWith("image/"))
            {
                res.StatusCode = 415;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "画像として認識できません" } });
                return;
            }
            string ext = DetectImageExtension(bytes);
            string resultMd5;
            using (var md5 = MD5.Create())
            {
                resultMd5 = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLower();
            }
            string requestedFileName = req.Headers["X-File-Name"];

            // Build 13 (13-2): 冪等確認 → ファイル書き込み → 商品登録 → ジョブへの記録を
            // claudeResultWriteLock で直列化し、同一 sourceMd5 の並行リトライによる二重書き込みを防ぐ。
            // ガードA: ロック取得直後にジョブを再取得し、最新状態で判定する (ロック外の古い参照で判定しない)。
            // ガードC: 既にエラー記録済みの sourceMd5 へは結果を登録しない (results と errors の相互排他)。
            lock (claudeResultWriteLock)
            {
                var fresh = ClaudeJobStore.GetJob(jobId);
                if (fresh == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                    return;
                }
                // レビュー 24-6: ロック内でも期限切れを再評価し、最新状態でトークンを再検証する
                ApplyTokenExpiry(fresh);
                fresh = ClaudeJobStore.GetJob(jobId);
                if (fresh == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                    return;
                }
                if (!ValidateClaudeJobToken(req, res, fresh)) return;
                if (fresh.status != "approved" && fresh.status != "processing")
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが実行可能な状態ではありません (status: " + fresh.status + ")" } });
                    return;
                }
                ClaudeJobPhoto freshSrcPhoto = null;
                if (fresh.photos != null)
                {
                    freshSrcPhoto = fresh.photos.Find(ph => string.Equals(ph.md5, sourceMd5, StringComparison.OrdinalIgnoreCase));
                }
                if (freshSrcPhoto == null)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "元写真がジョブに含まれていません" } });
                    return;
                }

                // 冪等性: 同一 sourceMd5 の結果が既にあれば既存レコードを成功として返す
                if (fresh.results != null)
                {
                    var existing = fresh.results.Find(r => string.Equals(r.sourceMd5, sourceMd5, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        ExtendClaudeJobToken(jobId);
                        SendJson(res, new Dictionary<string, object>
                        {
                            { "status", "ok" },
                            { "result", new Dictionary<string, object>
                                {
                                    { "sourceMd5", existing.sourceMd5 },
                                    { "resultFileName", existing.resultFileName },
                                    { "resultMd5", existing.resultMd5 }
                                } },
                            { "claudeRevision", GetClaudeRevision() }
                        });
                        return;
                    }
                }
                if (fresh.errors != null && fresh.errors.Exists(e => string.Equals(e.sourceMd5, sourceMd5, StringComparison.OrdinalIgnoreCase)))
                {
                    res.StatusCode = 409;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "この写真は既にエラーとして記録されています" } });
                    return;
                }

                // 商品を itemUuid 優先で再解決し、現在のフォルダを得る
                int currentItemId = 0;
                lock (lockObj)
                {
                    Dictionary<string, object> item = null;
                    if (!string.IsNullOrEmpty(fresh.itemUuid))
                    {
                        item = itemsDb.Find(i => i != null && i.ContainsKey("syncUuid") && i["syncUuid"] != null &&
                            string.Equals(i["syncUuid"].ToString(), fresh.itemUuid, StringComparison.OrdinalIgnoreCase));
                    }
                    if (item == null && fresh.itemId > 0)
                    {
                        item = itemsDb.Find(i => i != null && i.ContainsKey("id") && ParseIntSafe(i["id"], 0) == fresh.itemId);
                    }
                    if (item != null) currentItemId = ParseIntSafe(item["id"], 0);
                }
                if (currentItemId <= 0)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "商品が見つかりません" } });
                    return;
                }
                string folderPath = FindExistingItemFolder(currentItemId);
                if (string.IsNullOrEmpty(folderPath) && !string.IsNullOrEmpty(fresh.folderName))
                {
                    folderPath = Path.Combine(syncDir, fresh.folderName);
                }
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "商品フォルダが見つかりません" } });
                    return;
                }

                // 保存名 (Q4): ベース名の優先順位 + _ai + バイト判定の拡張子。同名は _ai2, _ai3... と連番
                string baseName = SanitizeClaudeResultBaseName(freshSrcPhoto.fileName);
                if (string.IsNullOrEmpty(baseName)) baseName = SanitizeClaudeResultBaseName(requestedFileName);
                if (string.IsNullOrEmpty(baseName))
                {
                    string srcPath = null;
                    lock (lockObj)
                    {
                        if (hashToFilePathMap.ContainsKey(sourceMd5)) srcPath = hashToFilePathMap[sourceMd5];
                    }
                    if (!string.IsNullOrEmpty(srcPath)) baseName = SanitizeClaudeResultBaseName(Path.GetFileName(srcPath));
                }
                if (string.IsNullOrEmpty(baseName))
                {
                    baseName = "photo_" + (sourceMd5.Length >= 8 ? sourceMd5.Substring(0, 8) : sourceMd5);
                }
                string resultFileName = baseName + "_ai" + ext;
                int seq = 2;
                while (File.Exists(Path.Combine(folderPath, resultFileName)) ||
                       IsIgnoredAutoImportPath(Path.Combine(folderPath, resultFileName)))
                {
                    resultFileName = baseName + "_ai" + seq + ext;
                    seq++;
                }
                string resultPath = Path.Combine(folderPath, resultFileName);

                File.WriteAllBytes(resultPath, bytes);

                // レビュー 24-6: 追記直前にも状態を再確認する (取消・失効で閉じたジョブへは書き込まない)。
                // 商品への写真登録は追記の成功後に行い、失敗時は書き込んだファイルを掃除する。
                bool resultAppended = false;
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    if (j.status != "approved" && j.status != "processing") return false;
                    if (j.results == null) j.results = new List<ClaudeJobResult>();
                    j.results.Add(new ClaudeJobResult { sourceMd5 = sourceMd5, resultFileName = resultFileName, resultMd5 = resultMd5 });
                    j.jobTokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30L * 60L * 1000L;
                    resultAppended = true;
                    return true;
                });
                if (!resultAppended)
                {
                    try { File.Delete(resultPath); } catch (Exception) { }
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが既に終了しているため結果を登録できませんでした" } });
                    return;
                }

                // フォルダ取込スキャン (フェーズ 4-4) と同一の登録処理で即時登録する。
                // photoStatCache / hashToFilePathMap / item["photos"] がロック下で即時更新され、
                // FileSystemWatcher 側の再取込・二重登録を抑制する。
                var fi = new FileInfo(resultPath);
                var entries = new List<PendingAutoImportPhoto>();
                entries.Add(new PendingAutoImportPhoto
                {
                    filePath = resultPath,
                    itemId = currentItemId,
                    md5 = resultMd5,
                    length = fi.Length,
                    lastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks
                });
                CommitAutoImportedPhotosBatch(entries);

                BumpClaudeRevision();

                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "result", new Dictionary<string, object>
                        {
                            { "sourceMd5", sourceMd5 },
                            { "resultFileName", resultFileName },
                            { "resultMd5", resultMd5 }
                        } },
                    { "claudeRevision", GetClaudeRevision() }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandlePostClaudeJobResult エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    // Build 13 (13-3): Claude 側から写真単位のエラーを報告する。
    // 生成結果の却下など、写真取得を伴わない失敗でも全写真が「結果またはエラー」へ到達できるようにし、
    // complete の前提条件が常に満たし得ることを保証する。results と同じ直列化ロックで保護する。
    private static void HandlePostClaudeJobError(HttpListenerRequest req, HttpListenerResponse res, string jobId)
    {
        try
        {
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            ApplyTokenExpiry(job);
            job = ClaudeJobStore.GetJob(jobId);
            if (!ValidateClaudeJobToken(req, res, job)) return;

            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            string sourceMd5 = "";
            string message = "";
            if (!string.IsNullOrEmpty(jsonStr))
            {
                try
                {
                    JObject json = JObject.Parse(jsonStr);
                    if (json["sourceMd5"] != null) sourceMd5 = json["sourceMd5"].ToString().Trim().ToLowerInvariant();
                    if (json["message"] != null) message = json["message"].ToString();
                }
                catch { }
            }
            if (string.IsNullOrEmpty(sourceMd5))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "sourceMd5 が必要です" } });
                return;
            }
            if (string.IsNullOrEmpty(message)) message = "Muse側でエラーが報告されました";
            if (message.Length > 500) message = message.Substring(0, 500);

            lock (claudeResultWriteLock)
            {
                var fresh = ClaudeJobStore.GetJob(jobId);
                if (fresh == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                    return;
                }
                // レビュー 24-6: ロック内でも期限切れを再評価し、最新状態でトークンを再検証する
                ApplyTokenExpiry(fresh);
                fresh = ClaudeJobStore.GetJob(jobId);
                if (fresh == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                    return;
                }
                if (!ValidateClaudeJobToken(req, res, fresh)) return;
                if (fresh.status != "approved" && fresh.status != "processing")
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが実行可能な状態ではありません (status: " + fresh.status + ")" } });
                    return;
                }
                bool inPhotos = fresh.photos != null &&
                    fresh.photos.Exists(ph => string.Equals(ph.md5, sourceMd5, StringComparison.OrdinalIgnoreCase));
                if (!inPhotos)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "元写真がジョブに含まれていません" } });
                    return;
                }
                // ガードC: 結果が登録済みの写真はエラーにできない (results と errors の相互排他)
                if (fresh.results != null && fresh.results.Exists(r => string.Equals(r.sourceMd5, sourceMd5, StringComparison.OrdinalIgnoreCase)))
                {
                    res.StatusCode = 409;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "この写真には既に結果が登録されています" } });
                    return;
                }
                // 冪等性: 同一 sourceMd5 のエラーが既にあれば既存レコードを成功として返す
                if (fresh.errors != null)
                {
                    var existingError = fresh.errors.Find(e => string.Equals(e.sourceMd5, sourceMd5, StringComparison.OrdinalIgnoreCase));
                    if (existingError != null)
                    {
                        ExtendClaudeJobToken(jobId);
                        SendJson(res, new Dictionary<string, object>
                        {
                            { "status", "ok" },
                            { "error", new Dictionary<string, object>
                                {
                                    { "sourceMd5", existingError.sourceMd5 },
                                    { "message", existingError.message }
                                } },
                            { "claudeRevision", GetClaudeRevision() }
                        });
                        return;
                    }
                }

                // レビュー 24-6: 追記直前にも状態を再確認する (結果側と同型)
                bool errorAppended = false;
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    if (j.status != "approved" && j.status != "processing") return false;
                    if (j.errors == null) j.errors = new List<ClaudeJobError>();
                    j.errors.Add(new ClaudeJobError { sourceMd5 = sourceMd5, message = message });
                    if (j.status == "approved") j.status = "processing";
                    j.jobTokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30L * 60L * 1000L;
                    errorAppended = true;
                    return true;
                });
                if (!errorAppended)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが既に終了しているためエラーを記録できませんでした" } });
                    return;
                }
                BumpClaudeRevision();

                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "error", new Dictionary<string, object>
                        {
                            { "sourceMd5", sourceMd5 },
                            { "message", message }
                        } },
                    { "claudeRevision", GetClaudeRevision() }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandlePostClaudeJobError エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleCompleteClaudeJob(HttpListenerRequest req, HttpListenerResponse res, string jobId)
    {
        try
        {
            // レビュー 24-6: 結果登録系と同じロックで直列化する (ロック順序はガードBどおり最外層)
            lock (claudeResultWriteLock)
            {
                HandleCompleteClaudeJobLocked(req, res, jobId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleCompleteClaudeJob エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    // フェーズ 6-6 是正 (Build 10 / §41.2・§42): 対象商品のまとめ UUID 集合を、最新の商品データから算出する。
    // 最上位の親まで辿り、その子孫 (mergedParentId 連鎖) と、写真の childItemId が指す商品 (とその子孫) を固定点まで集める。
    // 対象商品自身の UUID は必ず含める。商品が見つからない場合は対象 UUID のみを返す。
    private static List<string> CollectBundleUuidSetForItemUuid(string itemUuid)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(itemUuid)) return result;
        result.Add(itemUuid);
        lock (lockObj)
        {
            var byId = new Dictionary<int, Dictionary<string, object>>();
            var byUuid = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in itemsDb)
            {
                if (it == null) continue;
                int id = it.ContainsKey("id") ? ParseIntSafe(it["id"], 0) : 0;
                if (id > 0) byId[id] = it;
                string uuid = it.ContainsKey("syncUuid") && it["syncUuid"] != null ? it["syncUuid"].ToString().Trim() : "";
                if (!string.IsNullOrEmpty(uuid)) byUuid[uuid] = it;
            }
            Dictionary<string, object> target;
            if (!byUuid.TryGetValue(itemUuid.Trim(), out target)) return result;
            // 最上位の親まで辿る (循環ガード付き)
            var top = target;
            var guard = new HashSet<int>();
            while (true)
            {
                int pid = top.ContainsKey("mergedParentId") ? ParseIntSafe(top["mergedParentId"], 0) : 0;
                if (pid <= 0) break;
                int topId = top.ContainsKey("id") ? ParseIntSafe(top["id"], 0) : 0;
                if (topId > 0 && !guard.Add(topId)) break;
                Dictionary<string, object> parent;
                if (!byId.TryGetValue(pid, out parent) || parent == top) break;
                top = parent;
            }
            // 子孫と childItemId 参照を固定点まで集める
            var collected = new HashSet<Dictionary<string, object>>();
            collected.Add(top);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var it in itemsDb)
                {
                    if (it == null || collected.Contains(it)) continue;
                    int pid = it.ContainsKey("mergedParentId") ? ParseIntSafe(it["mergedParentId"], 0) : 0;
                    if (pid > 0 && byId.ContainsKey(pid) && collected.Contains(byId[pid])) { collected.Add(it); grew = true; }
                }
                var snapshot = new List<Dictionary<string, object>>(collected);
                foreach (var host in snapshot)
                {
                    if (!host.ContainsKey("photos") || host["photos"] == null) continue;
                    var photosList = SyncStore.NormalizePhotosList(host["photos"]);
                    foreach (var ph in photosList)
                    {
                        if (ph == null || !ph.ContainsKey("childItemId")) continue;
                        int cid = ParseIntSafe(ph["childItemId"], 0);
                        if (cid > 0 && byId.ContainsKey(cid) && !collected.Contains(byId[cid])) { collected.Add(byId[cid]); grew = true; }
                    }
                }
            }
            var uuidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            uuidSet.Add(itemUuid);
            foreach (var it in collected)
            {
                string uuid = it.ContainsKey("syncUuid") && it["syncUuid"] != null ? it["syncUuid"].ToString().Trim() : "";
                if (!string.IsNullOrEmpty(uuid)) uuidSet.Add(uuid);
            }
            result = new List<string>(uuidSet);
        }
        return result;
    }

    private static void HandleCompleteClaudeJobLocked(HttpListenerRequest req, HttpListenerResponse res, string jobId)
    {
        var job = ClaudeJobStore.GetJob(jobId);
        if (job == null)
        {
            res.StatusCode = 404;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
            return;
        }
        ApplyTokenExpiry(job);
        job = ClaudeJobStore.GetJob(jobId);
        if (!ValidateClaudeJobToken(req, res, job)) return;
        if (job.status != "approved" && job.status != "processing")
        {
            res.StatusCode = 400;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "完了できる状態ではありません (status: " + job.status + ")" } });
            return;
        }

        int missing = 0;
        if (job.photos != null)
        {
            foreach (var ph in job.photos)
            {
                bool hasResult = job.results != null && job.results.Exists(r => string.Equals(r.sourceMd5, ph.md5, StringComparison.OrdinalIgnoreCase));
                bool hasError = job.errors != null && job.errors.Exists(e => string.Equals(e.sourceMd5, ph.md5, StringComparison.OrdinalIgnoreCase));
                if (!hasResult && !hasError) missing++;
            }
        }
        if (missing > 0)
        {
            res.StatusCode = 400;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "未処理の写真が " + missing + " 枚あります（結果またはエラーの記録が必要です）" } });
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string newStatus = (job.errors != null && job.errors.Count > 0) ? "done_with_errors" : "done";
        // フェーズ 6-6 是正 (Build 10 / §41.2): 自動解消の範囲をまとめ UUID 集合に広げる (画面判定と一致させる)
        var bundleUuids = CollectBundleUuidSetForItemUuid(job.itemUuid);
        ClaudeJobStore.MutateJobWithErrorSupersede(jobId, j =>
        {
            j.status = newStatus;
            j.completedAt = now;
            j.jobToken = null;
            j.jobTokenExpiresAt = 0;
            return true;
        }, bundleUuids);
        BumpClaudeRevision();
        job = ClaudeJobStore.GetJob(jobId);
        SendJson(res, new Dictionary<string, object>
        {
            { "status", "ok" },
            { "job", ToPublicClaudeJob(job) },
            { "claudeRevision", GetClaudeRevision() }
        });
    }

    private static void HandleAckClaudeJob(HttpListenerResponse res, string jobId)
    {
        try
        {
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            if (!job.acknowledged)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    j.acknowledged = true;
                    j.acknowledgedAt = now;
                    return true;
                });
                BumpClaudeRevision();
                job = ClaudeJobStore.GetJob(jobId);
            }
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", ToPublicClaudeJob(job) },
                { "claudeRevision", GetClaudeRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleAckClaudeJob エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    // ジョブの所属リスト (queue) を移す (フェーズ 6-1a)。
    // 巡回・手動への移動は「送信」操作として承認リクエストを発火し、
    // 待機への移動はリクエストの取り下げになる。同じ所属への移動は冪等 (no-op)。
    private static void HandleMoveClaudeJob(HttpListenerRequest req, HttpListenerResponse res, string jobId)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            string target = "";
            if (!string.IsNullOrEmpty(jsonStr))
            {
                JObject json = JObject.Parse(jsonStr);
                target = (json.Value<string>("queue") ?? "").Trim();
            }
            if (target != "patrol" && target != "manual" && target != "waiting")
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "移動先の指定が不正です" } });
                return;
            }
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            ApplyApprovalPendingExpiry(job);
            ApplyTokenExpiry(job);
            job = ClaudeJobStore.GetJob(jobId);
            string currentQueue = string.IsNullOrEmpty(job.queue) ? "manual" : job.queue;
            if (currentQueue == target)
            {
                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "job", ToPublicClaudeJob(job) },
                    { "claudeRevision", GetClaudeRevision() }
                });
                return;
            }
            if (job.status != "queued" && job.status != "approval_pending")
            {
                res.StatusCode = 409;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "現在の状態では移動できません (status: " + job.status + ")" } });
                return;
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ClaudeJobStore.MutateJob(jobId, j =>
            {
                j.queue = target;
                if (target == "waiting")
                {
                    j.status = "queued";
                    j.approvalRequestedAt = 0;
                }
                else if (j.status == "queued")
                {
                    j.status = "approval_pending";
                    j.approvalRequestedAt = now;
                }
                return true;
            });
            BumpClaudeRevision();
            job = ClaudeJobStore.GetJob(jobId);
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", ToPublicClaudeJob(job) },
                { "claudeRevision", GetClaudeRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleMoveClaudeJob エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    // エラー警告の解消 (フェーズ 6-1a)。通知の確認 (ack) とは別のフラグで管理する。
    // 冪等仕様は HandleAckClaudeJob と同型 (変化したときだけ保存とリビジョン更新)。
    // フェーズ 6-5: 加工済みラベルの既読化 (商品カードを開いた端末から呼ばれる)。
    private static void HandleMarkClaudeLabelSeen(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            var itemUuids = new List<string>();
            if (!string.IsNullOrEmpty(jsonStr))
            {
                JObject json = JObject.Parse(jsonStr);
                string single = (json.Value<string>("itemUuid") ?? "").Trim();
                if (single.Length > 0) itemUuids.Add(single);
                var arr = json["itemUuids"] as JArray;
                if (arr != null)
                {
                    foreach (var el in arr)
                    {
                        string u = (el != null ? el.ToString() : "").Trim();
                        if (u.Length > 0) itemUuids.Add(u);
                    }
                }
            }
            if (itemUuids.Count == 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "itemUuid が必要です" } });
                return;
            }
            long seenAt = ClaudeJobStore.MarkLabelsSeen(itemUuids);
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "seenAt", seenAt },
                { "labelSeenRevision", ClaudeJobStore.GetLabelSeenRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleMarkClaudeLabelSeen エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleDismissClaudeJobError(HttpListenerResponse res, string jobId)
    {
        try
        {
            var job = ClaudeJobStore.GetJob(jobId);
            if (job == null)
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                return;
            }
            if (job.status != "done_with_errors")
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "エラー警告を解消できる状態ではありません (status: " + job.status + ")" } });
                return;
            }
            if (!job.errorDismissed)
            {
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    j.errorDismissed = true;
                    return true;
                });
                BumpClaudeRevision();
                job = ClaudeJobStore.GetJob(jobId);
            }
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", ToPublicClaudeJob(job) },
                { "claudeRevision", GetClaudeRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleDismissClaudeJobError エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleCancelClaudeJob(HttpListenerResponse res, string jobId)
    {
        try
        {
            // レビュー 24-6: 結果登録系と同じロックで直列化する (ロック順序はガードBどおり最外層)
            lock (claudeResultWriteLock)
            {
                HandleCancelClaudeJobLocked(res, jobId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleCancelClaudeJob エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleCancelClaudeJobLocked(HttpListenerResponse res, string jobId)
    {
        int result = ClaudeJobStore.CancelJob(jobId);
        if (result == 0)
        {
            res.StatusCode = 404;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
            return;
        }
        if (result == 2)
        {
            res.StatusCode = 400;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "このジョブは取り消せない状態です" } });
            return;
        }
        BumpClaudeRevision();
        var job = ClaudeJobStore.GetJob(jobId);
        SendJson(res, new Dictionary<string, object>
        {
            { "status", "ok" },
            { "job", ToPublicClaudeJob(job) },
            { "claudeRevision", GetClaudeRevision() }
        });
    }

    private static string DetectImageMimeType(byte[] bytes)
    {
        if (bytes != null)
        {
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return "image/png";
            }
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            {
                return "image/webp";
            }
            if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            {
                return "image/gif";
            }
            if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            {
                return "image/bmp";
            }
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "image/jpeg";
            }
        }
        // Build 13 (13-1): どの画像形式にも一致しない場合は空文字を返す。
        // 旧実装は無条件に image/jpeg を返していたため、呼び出し側の画像検証が無効化されていた。
        return "";
    }

    private static string DetectImageExtension(byte[] bytes)
    {
        string mime = DetectImageMimeType(bytes);
        if (mime == "image/png") return ".png";
        if (mime == "image/webp") return ".webp";
        if (mime == "image/gif") return ".gif";
        if (mime == "image/bmp") return ".bmp";
        return ".jpg";
    }

    private static void HandleScanItemFolderPhotos(HttpListenerResponse res, string path)
    {
        try
        {
            string idStr = path.Substring("/api/items/".Length);
            idStr = idStr.Substring(0, idStr.Length - "/photos/scan".Length).Trim();
            int itemId;
            if (!int.TryParse(idStr, out itemId) || itemId <= 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid ID" } });
                return;
            }

            string itemDir = null;
            Dictionary<string, PhotoFileCacheEntry> cacheSnapshot;
            lock (lockObj)
            {
                var item = itemsDb.Find(i => i != null && i.ContainsKey("id") && ParseIntSafe(i["id"], 0) == itemId);
                if (item == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Item not found" } });
                    return;
                }
                itemDir = FindExistingItemFolder(itemId);
                cacheSnapshot = new Dictionary<string, PhotoFileCacheEntry>(photoStatCache, StringComparer.OrdinalIgnoreCase);
            }

            if (string.IsNullOrEmpty(itemDir) || !Directory.Exists(itemDir))
            {
                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "ok" },
                    { "importedCount", 0 },
                    { "dataRevision", GetDataRevision() }
                });
                return;
            }

            var scannedEntries = new List<PendingAutoImportPhoto>();
            using (var md5 = MD5.Create())
            {
                foreach (string file in Directory.GetFiles(itemDir, "*.*", SearchOption.AllDirectories))
                {
                    if (IsIgnoredAutoImportPath(file) || !IsSupportedImageExtension(file)) continue;
                    try
                    {
                        FileInfo fi = new FileInfo(file);
                        if (!fi.Exists || fi.Length <= 0) continue;
                        long len = fi.Length;
                        long ticks = fi.LastWriteTimeUtc.Ticks;

                        PhotoFileCacheEntry cached;
                        if (cacheSnapshot.TryGetValue(file, out cached) &&
                            cached != null && cached.length == len && cached.lastWriteUtcTicks == ticks &&
                            !string.IsNullOrEmpty(cached.md5))
                        {
                            scannedEntries.Add(new PendingAutoImportPhoto
                            {
                                filePath = file,
                                itemId = itemId,
                                md5 = cached.md5,
                                length = len,
                                lastWriteUtcTicks = ticks
                            });
                            continue;
                        }

                        byte[] header = new byte[16];
                        int headerRead = 0;
                        string hashStr = "";
                        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            headerRead = fs.Read(header, 0, header.Length);
                            if (!IsValidImageHeader(header, headerRead)) continue;
                            fs.Position = 0;
                            byte[] hashBytes = md5.ComputeHash(fs);
                            hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                        }

                        if (!string.IsNullOrEmpty(hashStr))
                        {
                            scannedEntries.Add(new PendingAutoImportPhoto
                            {
                                filePath = file,
                                itemId = itemId,
                                md5 = hashStr,
                                length = len,
                                lastWriteUtcTicks = ticks
                            });
                        }
                    }
                    catch { }
                }
            }

            int importedCount = CommitAutoImportedPhotosBatch(scannedEntries);
            if (importedCount > 0)
            {
                Console.WriteLine("[フォルダ取込] 商品ID " + itemId + " から " + importedCount + " 枚の新規写真を取り込みました。");
            }

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "importedCount", importedCount },
                { "dataRevision", GetDataRevision() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleScanItemFolderPhotos エラー: " + ex.Message);
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
                string photoContentType = DetectImageMimeType(bytes);
                res.ContentType = string.IsNullOrEmpty(photoContentType) ? "application/octet-stream" : photoContentType;
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

