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
            bool tooLarge;
            if (!TryReadStreamWithLimit(req.InputStream, MaxPhotoUploadBytes, out photoBytes, out tooLarge))
            {
                res.StatusCode = tooLarge ? 413 : 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", tooLarge ? "ファイルサイズ上限 (50MB) を超過しています" : "画像データの読み込みに失敗しました" } });
                return;
            }

            if (photoBytes.Length == 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "画像データが空です" } });
                return;
            }
            if (!IsValidImageHeader(photoBytes, photoBytes.Length))
            {
                res.StatusCode = 415;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "有効な画像形式ではありません" } });
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
                    AtomicFileWriter.WriteBytesAtomic(photoFilePath, photoBytes, false);
                }
                var addFi = new FileInfo(photoFilePath);
                hashToFilePathMap[computedHash] = photoFilePath;
                photoStatCache[photoFilePath] = new PhotoFileCacheEntry
                {
                    length = addFi.Length,
                    lastWriteUtcTicks = addFi.LastWriteTimeUtc.Ticks,
                    md5 = computedHash
                };

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
        public int photoIndex = -1;
        public int photoId = 0;
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

    private static int TrashPhotosFromTargets(List<PhotoTrashTarget> targets, out int remainingCountFirstItem, out int removedBrokenCount)
    {
        remainingCountFirstItem = 0;
        removedBrokenCount = 0;
        if (targets == null || targets.Count == 0) return 0;

        var newTrashEntries = new List<PhotoTrashEntry>();
        var touchedHashes = new List<string>();
        var pendingTrashMeta = new List<PhotoTrashEntry>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool anyRemoved = false;

        lock (lockObj)
        {
            int firstTargetItemId = targets[0] != null ? targets[0].itemId : 0;

            // 1. 空MD5の壊れた写真参照（ゴミ箱退避対象外・インデックスずれ防止のため元のリスト基準で一括解決）
            var brokenByItem = new Dictionary<int, List<PhotoTrashTarget>>();
            foreach (var t in targets)
            {
                if (t == null || t.itemId <= 0) continue;
                if (string.IsNullOrEmpty(t.md5Hash) && (t.photoIndex >= 0 || t.photoId > 0))
                {
                    if (!brokenByItem.ContainsKey(t.itemId)) brokenByItem[t.itemId] = new List<PhotoTrashTarget>();
                    brokenByItem[t.itemId].Add(t);
                }
            }

            foreach (var kv in brokenByItem)
            {
                int itemId = kv.Key;
                var item = itemsDb.Find(i => ParseIntSafe(i["id"]) == itemId);
                if (item == null) continue;

                var currentPhotos = item.ContainsKey("photos") && item["photos"] != null
                    ? SyncStore.NormalizePhotosList(item["photos"])
                    : new List<Dictionary<string, object>>();

                var matchedIndices = new HashSet<int>();
                // §83-5: 子商品側の追従除去用に、子商品IDごとに
                // 「親リスト内で当該子商品の写真として現れる順序 (0始まり)」と写真IDを記録する。
                // まとめ子商品の写真は Android 側で別 ID に採番し直されるため、
                // 親写真の id では子側を特定できない。位置 (順序) を主キーにし、
                // id が一致する場合はそれも併用する。
                var childFollowOrdinals = new Dictionary<int, HashSet<int>>();
                var childFollowPhotoIds = new Dictionary<int, HashSet<int>>();

                foreach (var bt in kv.Value)
                {
                    int matchIdx = -1;
                    // §83-5: photoId がある場合は id 一致を最優先する。photoIndex は
                    // 配列ずれ時に別の空 MD5 エントリを誤って指す恐れがあるため、
                    // photoId <= 0 の場合に限って使う。
                    if (bt.photoId > 0)
                    {
                        for (int idx = 0; idx < currentPhotos.Count; idx++)
                        {
                            if (matchedIndices.Contains(idx)) continue;
                            var candidate = currentPhotos[idx];
                            if (candidate == null) continue;
                            string ch = candidate.ContainsKey("md5Hash") && candidate["md5Hash"] != null
                                ? candidate["md5Hash"].ToString().Trim()
                                : "";
                            int cid = candidate.ContainsKey("id") ? ParseIntSafe(candidate["id"], 0) : 0;
                            if (string.IsNullOrEmpty(ch) && cid == bt.photoId)
                            {
                                matchIdx = idx;
                                break;
                            }
                        }
                    }
                    if (matchIdx < 0 && bt.photoId <= 0 && bt.photoIndex >= 0 && bt.photoIndex < currentPhotos.Count && !matchedIndices.Contains(bt.photoIndex))
                    {
                        var candidate = currentPhotos[bt.photoIndex];
                        string ch = candidate != null && candidate.ContainsKey("md5Hash") && candidate["md5Hash"] != null
                            ? candidate["md5Hash"].ToString().Trim()
                            : "";
                        if (string.IsNullOrEmpty(ch))
                        {
                            matchIdx = bt.photoIndex;
                        }
                    }
                    if (matchIdx >= 0)
                    {
                        matchedIndices.Add(matchIdx);
                        var matched = currentPhotos[matchIdx];
                        if (matched != null)
                        {
                            int pChildId = matched.ContainsKey("childItemId") ? ParseIntSafe(matched["childItemId"], 0) : 0;
                            if (pChildId <= 0) pChildId = bt.childItemId;
                            int pId = matched.ContainsKey("id") ? ParseIntSafe(matched["id"], 0) : 0;
                            if (pChildId > 0 && pChildId != itemId)
                            {
                                // 親リスト内で同じ childItemId を持つ写真の中での出現順序
                                int ordinal = 0;
                                for (int k = 0; k < matchIdx; k++)
                                {
                                    var prev = currentPhotos[k];
                                    if (prev == null) continue;
                                    int prevChildId = prev.ContainsKey("childItemId") ? ParseIntSafe(prev["childItemId"], 0) : 0;
                                    if (prevChildId == pChildId) ordinal++;
                                }
                                if (!childFollowOrdinals.ContainsKey(pChildId)) childFollowOrdinals[pChildId] = new HashSet<int>();
                                childFollowOrdinals[pChildId].Add(ordinal);
                                if (pId > 0)
                                {
                                    if (!childFollowPhotoIds.ContainsKey(pChildId)) childFollowPhotoIds[pChildId] = new HashSet<int>();
                                    childFollowPhotoIds[pChildId].Add(pId);
                                }
                            }
                        }
                    }
                }

                if (matchedIndices.Count > 0)
                {
                    var updatedPhotos = new List<Dictionary<string, object>>();
                    for (int idx = 0; idx < currentPhotos.Count; idx++)
                    {
                        if (matchedIndices.Contains(idx)) continue;
                        var dictCopy = currentPhotos[idx];
                        if (dictCopy == null) continue;
                        dictCopy["displayOrder"] = updatedPhotos.Count;
                        updatedPhotos.Add(dictCopy);
                    }
                    item["photos"] = updatedPhotos;
                    item["updatedAt"] = now;
                    item["syncedToPhone"] = false;
                    anyRemoved = true;
                    removedBrokenCount += matchedIndices.Count;
                    if (itemId == firstTargetItemId) remainingCountFirstItem = updatedPhotos.Count;

                    if (childFollowOrdinals.Count > 0)
                    {
                        // §83-5: 子商品側の追従除去。子商品は matched 写真自身の
                        // childItemId から特定する (リクエストの childItemId が 0 でも効く)。
                        // 子商品の写真リストは親リスト内の当該子商品ぶんの並びと対応するため、
                        // 同じ出現順序の位置にある空 MD5 写真を除去する (id 一致時も除去)。
                        foreach (var childKv in childFollowOrdinals)
                        {
                            int childId = childKv.Key;
                            var childItem = itemsDb.Find(c => ParseIntSafe(c["id"]) == childId);
                            if (childItem == null || !childItem.ContainsKey("photos") || childItem["photos"] == null) continue;
                            var childPhotos = SyncStore.NormalizePhotosList(childItem["photos"]);
                            var ordinals = childKv.Value;
                            HashSet<int> followPhotoIds;
                            childFollowPhotoIds.TryGetValue(childId, out followPhotoIds);
                            var updatedChildPhotos = new List<Dictionary<string, object>>();
                            bool childChanged = false;
                            for (int ci = 0; ci < childPhotos.Count; ci++)
                            {
                                var cp = childPhotos[ci];
                                if (cp == null) continue;
                                string ch = cp.ContainsKey("md5Hash") && cp["md5Hash"] != null ? cp["md5Hash"].ToString().Trim() : "";
                                int cpId = cp.ContainsKey("id") ? ParseIntSafe(cp["id"], 0) : 0;
                                bool isBrokenTarget = string.IsNullOrEmpty(ch) &&
                                    (ordinals.Contains(ci) || (followPhotoIds != null && cpId > 0 && followPhotoIds.Contains(cpId)));
                                if (isBrokenTarget)
                                {
                                    childChanged = true;
                                    continue;
                                }
                                cp["displayOrder"] = updatedChildPhotos.Count;
                                updatedChildPhotos.Add(cp);
                            }
                            if (childChanged)
                            {
                                childItem["photos"] = updatedChildPhotos;
                                childItem["updatedAt"] = now;
                                childItem["syncedToPhone"] = false;
                            }
                        }
                    }

                    Console.WriteLine("[救済削除] 空MD5の壊れた写真参照 " + matchedIndices.Count + "件をリストから直接除外しました (itemId=" + itemId + ")");
                }
            }

            // 2. 通常のMD5確定写真（写真ゴミ箱へ移動）
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

                if (target.itemId == firstTargetItemId)
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
                int snapPhotoCount, snapMissingCount;
                lock (lockObj)
                {
                    snapPhotoCount = hashToFilePathMap.Count;
                    snapMissingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
                }
                qrForm.UpdateStatusInfo(snapPhotoCount, snapMissingCount);
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
            int photoIndex = -1;
            int photoId = 0;
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
                    if (json["photoIndex"] != null && json["photoIndex"].Type != JTokenType.Null)
                    {
                        photoIndex = ParseIntSafe(json["photoIndex"], -1);
                    }
                    if (json["photoId"] != null && json["photoId"].Type != JTokenType.Null)
                    {
                        photoId = ParseIntSafe(json["photoId"], 0);
                    }
                }
            }

            if (string.IsNullOrEmpty(targetHash) && photoIndex < 0 && photoId <= 0)
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "削除対象の写真が指定されていません" } });
                return;
            }

            int remainingCount;
            int removedBrokenCount;
            int movedCount = TrashPhotosFromTargets(
                new List<PhotoTrashTarget>
                {
                    new PhotoTrashTarget
                    {
                        itemId = itemId,
                        md5Hash = targetHash,
                        childItemId = childItemId,
                        photoIndex = photoIndex,
                        photoId = photoId
                    }
                },
                out remainingCount,
                out removedBrokenCount
            );

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "removed", (movedCount + removedBrokenCount) > 0 },
                { "movedToTrashCount", movedCount },
                { "removedBrokenCount", removedBrokenCount },
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
                    int pIdx = obj["photoIndex"] != null && obj["photoIndex"].Type != JTokenType.Null
                        ? ParseIntSafe(obj["photoIndex"], -1)
                        : -1;
                    int pId = obj["photoId"] != null && obj["photoId"].Type != JTokenType.Null
                        ? ParseIntSafe(obj["photoId"], 0)
                        : 0;
                    if (tid > 0 && (!string.IsNullOrEmpty(hash) || pIdx >= 0 || pId > 0))
                    {
                        targets.Add(new PhotoTrashTarget
                        {
                            itemId = tid,
                            md5Hash = hash,
                            childItemId = cid,
                            photoIndex = pIdx,
                            photoId = pId
                        });
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
            int removedBrokenCount;
            int movedCount = TrashPhotosFromTargets(targets, out dummyRemaining, out removedBrokenCount);

            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "movedToTrashCount", movedCount },
                { "removedBrokenCount", removedBrokenCount },
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

    private static void ServePhotoTrashImage(HttpListenerRequest req, HttpListenerResponse res, string trashId)
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
                ServePhotoByHash(req, res, entry.md5Hash);
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
                    int snapPhotoCount, snapMissingCount;
                    lock (lockObj)
                    {
                        snapPhotoCount = hashToFilePathMap.Count;
                        snapMissingCount = SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap);
                    }
                    qrForm.UpdateStatusInfo(snapPhotoCount, snapMissingCount);
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



    internal static string DetectImageMimeType(byte[] bytes)
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

    internal static string DetectImageExtension(byte[] bytes)
    {
        string mime = DetectImageMimeType(bytes);
        if (mime == "image/png") return ".png";
        if (mime == "image/webp") return ".webp";
        if (mime == "image/gif") return ".gif";
        if (mime == "image/bmp") return ".bmp";
        return ".jpg";
    }



    internal static bool MatchesIfNoneMatch(string ifNoneMatchHeader, string etag, string rawHash)
    {
        if (string.IsNullOrEmpty(ifNoneMatchHeader) || string.IsNullOrEmpty(etag)) return false;
        string[] parts = ifNoneMatchHeader.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i].Trim();
            if (token == "*") return true;
            if (token.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring(2).Trim();
            }
            if (string.Equals(token, etag, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(rawHash) && string.Equals(token.Trim('"'), rawHash, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void ServePhotoByHash(HttpListenerRequest req, HttpListenerResponse res, string hash)
    {
        string filePath = null;
        lock (lockObj)
        {
            if (!hashToFilePathMap.TryGetValue(hash, out filePath))
            {
                filePath = null;
            }
        }
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            string cleanHash = !string.IsNullOrEmpty(hash) ? hash.Replace("\"", "") : "";
            string etag = !string.IsNullOrEmpty(cleanHash) ? ("\"" + cleanHash + "\"") : "";
            if (!string.IsNullOrEmpty(etag) && MatchesIfNoneMatch(req != null ? req.Headers["If-None-Match"] : null, etag, cleanHash))
            {
                res.StatusCode = 304;
                res.Headers.Add("Cache-Control", "public, max-age=31536000, immutable");
                res.Headers.Add("ETag", etag);
                res.Close();
                return;
            }
            byte[] bytes = File.ReadAllBytes(filePath);
            string photoContentType = DetectImageMimeType(bytes);
            res.ContentType = string.IsNullOrEmpty(photoContentType) ? "application/octet-stream" : photoContentType;
            res.Headers.Add("Cache-Control", "public, max-age=31536000, immutable");
            if (!string.IsNullOrEmpty(etag))
            {
                res.Headers.Add("ETag", etag);
            }
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
            return;
        }
        res.StatusCode = 404;
        res.Close();
    }
}
