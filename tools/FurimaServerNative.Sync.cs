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
                var pendingSyncTxtWrites = new List<KeyValuePair<string, string>>();

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
                            string txtContent = "【商品ID】 " + assignedId + "\r\n【タイトル】 " + title + "\r\n【販売価格】 ¥" + price.ToString("N0") + "\r\n【純利益】 ¥" + profit.ToString("N0") + "\r\n【ステータス】 " + statusLabel + "\r\n\r\n--- 商品説明 ---\r\n" + description;
                            pendingSyncTxtWrites.Add(new KeyValuePair<string, string>(txtPath, txtContent));

                            JArray photosArr = item["photos"] as JArray;
                            List<Dictionary<string, object>> savedPhotosList = new List<Dictionary<string, object>>();

                            if (photosArr != null)
                            {
                                foreach (JObject photo in photosArr)
                                {
                                    int pId = ParseIntSafe(photo["id"], 0);
                                    string md5Hash = (photo.Value<string>("md5Hash") ?? "").Trim().ToLower();
                                    if (string.IsNullOrEmpty(md5Hash))
                                    {
                                        Console.WriteLine("[警告] 空MD5の写真エントリを同期受信時に除外しました (itemId=" + assignedId + ", photoId=" + pId + ")");
                                        continue;
                                    }

                                    bool existsOnServer = hashToFilePathMap.ContainsKey(md5Hash) &&
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
                    }
                }

                foreach (var tw in pendingSyncTxtWrites)
                {
                    try { File.WriteAllText(tw.Key, tw.Value, Encoding.UTF8); } catch { }
                }

                if (dataChanged)
                {
                    BumpDataRevision();
                    SaveItemsDbToDisk();
                }
                if (itemsArr != null)
                {
                    ScanAndIndexLocalPhotosIfStale(30);
                }

                JObject dsObj = json["defaultSettings"] as JObject;
                if (dsObj != null)
                {
                    try
                    {
                        var incomingCfg = ParseDefaultSettingsConfig(dsObj);
                        // フェーズ 6-8 (§40.4): 比較〜保存は SyncStore の原子的メソッドへ寄せる
                        // (テンプレート登録と競合しても、比較と保存の間に割り込まれない)
                        if (SyncStore.SaveDefaultSettingsIfNewer(incomingCfg))
                        {
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

            byte[] photoBytes;
            bool tooLarge;
            if (!TryReadStreamWithLimit(req.InputStream, MaxPhotoUploadBytes, out photoBytes, out tooLarge))
            {
                res.StatusCode = tooLarge ? 413 : 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "stored", false }, { "message", tooLarge ? "ファイルサイズ上限 (50MB) を超過しています" : "画像データの読み込みに失敗しました" } });
                return;
            }
            if (photoBytes.Length == 0 || !IsValidImageHeader(photoBytes, photoBytes.Length))
            {
                res.StatusCode = 415;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "stored", false }, { "message", "有効な画像形式ではありません" } });
                return;
            }
            {
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
                    AtomicFileWriter.WriteBytesAtomic(photoPath, photoBytes, false);
                    var syncFi = new FileInfo(photoPath);

                    lock (lockObj)
                    {
                        hashToFilePathMap[computedHash] = photoPath;
                        photoStatCache[photoPath] = new PhotoFileCacheEntry
                        {
                            length = syncFi.Length,
                            lastWriteUtcTicks = syncFi.LastWriteTimeUtc.Ticks,
                            md5 = computedHash
                        };
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
}
