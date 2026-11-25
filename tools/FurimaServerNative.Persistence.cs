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
        // フェーズ 6-13 (§47.11-1): 参考画像の 3 ディレクトリも自動取り込みの対象外にする
        string refInboxPrefix = Path.Combine(syncDir, "_reference_inbox");
        if (fullPath.StartsWith(refInboxPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        string refPhotosPrefix = Path.Combine(syncDir, "_reference_photos");
        if (fullPath.StartsWith(refPhotosPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        string refThumbsPrefix = Path.Combine(syncDir, "_reference_thumbs");
        if (fullPath.StartsWith(refThumbsPrefix, StringComparison.OrdinalIgnoreCase)) return true;
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
        Interlocked.Exchange(ref lastFullPhotoScanTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref missingCountDirty, 1);
        Console.WriteLine("インデックス完了: " + nextMap.Count + "件のユニーク写真ハッシュをロードしました。");
    }

    private static void ScanAndIndexLocalPhotosIfStale(int minIntervalSec)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long prevTicks = Interlocked.Read(ref lastFullPhotoScanTicks);
        if (prevTicks > 0 && (nowTicks - prevTicks) < (long)minIntervalSec * TimeSpan.TicksPerSecond)
        {
            return;
        }
        ScanAndIndexLocalPhotos();
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
            if (File.Exists(itemsJsonFile) || File.Exists(itemsJsonFile + ".bak"))
            {
                bool recovered;
                string errDetail;
                var loaded = AtomicFileWriter.ReadJsonWithBackup<List<Dictionary<string, object>>>(
                    itemsJsonFile,
                    list => list != null,
                    out recovered,
                    out errDetail
                );
                if (loaded != null)
                {
                    itemsDb = loaded;
                    if (recovered) Console.WriteLine("[復旧] items_data.json を .bak から復元しました");
                }
                else
                {
                    string prevPath = Path.Combine(Path.GetDirectoryName(itemsJsonFile) ?? "", "items_data.prev.json");
                    bool prevLoaded = false;
                    if (File.Exists(prevPath))
                    {
                        try
                        {
                            string pJson = File.ReadAllText(prevPath, Encoding.UTF8);
                            var pList = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(pJson);
                            if (pList != null)
                            {
                                itemsDb = pList;
                                prevLoaded = true;
                                Console.WriteLine("[復旧] items_data.json を items_data.prev.json から復元しました");
                                try { AtomicFileWriter.WriteTextAtomic(itemsJsonFile, pJson, false); } catch { }
                            }
                        }
                        catch { }
                    }
                    if (!prevLoaded)
                    {
                        string corruptPath = AtomicFileWriter.QuarantineCorruptFile(itemsJsonFile);
                        Console.WriteLine("[重大警告] items_data.json ロード例外 (" + errDetail + ")" + (string.IsNullOrEmpty(corruptPath) ? "" : " 退避先: " + corruptPath));
                    }
                }
            }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long cutoffMs = nowMs - (30L * 24L * 60L * 60L * 1000L);
            HashSet<int> purgeIds = new HashSet<int>();
            List<string> purgeUuids = new List<string>();
            bool cleanedBrokenPhotos = false;
            int brokenRemovedCount = 0;

            foreach (var item in itemsDb)
            {
                if (item == null) continue;
                bool hadUuid = item.ContainsKey("syncUuid") && item["syncUuid"] != null && !string.IsNullOrEmpty(item["syncUuid"].ToString().Trim());
                string uuid = EnsureItemSyncUuid(item);
                if (!hadUuid) changed = true;

                // Build 17: 空MD5かつ実ファイル参照不能な壊れ写真参照を起動時に自動浄化
                if (item.ContainsKey("photos") && item["photos"] != null)
                {
                    var rawPhotos = SyncStore.NormalizePhotosList(item["photos"]);
                    var validPhotos = new List<Dictionary<string, object>>();
                    bool itemBrokenRemoved = false;
                    foreach (var p in rawPhotos)
                    {
                        if (p == null)
                        {
                            itemBrokenRemoved = true;
                            brokenRemovedCount++;
                            continue;
                        }
                        string h = p.ContainsKey("md5Hash") && p["md5Hash"] != null
                            ? p["md5Hash"].ToString().Trim().ToLower()
                            : "";
                        string fp = p.ContainsKey("filePath") && p["filePath"] != null
                            ? p["filePath"].ToString().Trim()
                            : "";
                        bool hasRealFile = !string.IsNullOrEmpty(fp) && File.Exists(fp);
                        if (string.IsNullOrEmpty(h) && !hasRealFile)
                        {
                            itemBrokenRemoved = true;
                            brokenRemovedCount++;
                            continue;
                        }
                        p["displayOrder"] = validPhotos.Count;
                        validPhotos.Add(p);
                    }
                    if (itemBrokenRemoved)
                    {
                        item["photos"] = validPhotos;
                        // レビュー §83-3: updatedAt は引き上げない。現在時刻へ更新すると、
                        // スマホ側の未同期 (isDirty) 編集より新しくなり、次回 Pull で
                        // isDirty 保護 (updatedAt 比較) が破れて未同期編集が上書き消失する。
                        // スマホへの再同期トリガーは syncedToPhone = false と
                        // BumpDataRevision() のみで行う。
                        item["syncedToPhone"] = false;
                        cleanedBrokenPhotos = true;
                        changed = true;
                    }
                }

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

            if (cleanedBrokenPhotos)
            {
                try
                {
                    string emptyMd5Backup = itemsJsonFile + ".bak_empty_md5";
                    if (File.Exists(itemsJsonFile) && !File.Exists(emptyMd5Backup))
                    {
                        File.Copy(itemsJsonFile, emptyMd5Backup, false);
                        Console.WriteLine("[整合性修復] 初回バックアップを作成しました: " + emptyMd5Backup);
                    }
                }
                catch (Exception bakEx)
                {
                    Console.WriteLine("[警告] 空MD5浄化前バックアップ作成失敗: " + bakEx.Message);
                }
                BumpDataRevision();
                Console.WriteLine("[整合性修復] 空MD5の壊れ写真参照 " + brokenRemovedCount + "件を除外しました");
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
                AtomicFileWriter.WriteTextAtomic(itemsJsonFile, json, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("items_data.json 保存例外: " + ex.Message);
            }
        }
    }
}
