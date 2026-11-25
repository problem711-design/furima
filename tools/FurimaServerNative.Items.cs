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


    // フェーズ 6-4: 売却系ステータス {SOLD, SHIPPING} の共通判定。
    // 売却価格・売却日時の保持、利益計算、売上集計はすべてこの所属判定に揃える。
    private static bool IsSoldFamily(string status)
    {
        return status == "SOLD" || status == "SHIPPING";
    }

    // フェーズ 6 是正 (Build 13 / 49-4): クライアントから受け付ける商品ステータスのホワイトリスト。
    // MERGED はシステムが付与する専用状態のため、API 入力としては受け付けない。
    private static bool IsValidItemStatus(string status)
    {
        return status == "DRAFT" || status == "DRAFT_DONE" || status == "LISTED" || status == "SHIPPING" || status == "SOLD";
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
                if (!IsValidItemStatus(status))
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "不正なステータスです: " + status } });
                    return;
                }
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
            if (newStatus != null && !IsValidItemStatus(newStatus))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "不正なステータスです: " + newStatus } });
                return;
            }

            bool applyCostPreset = ParseBoolSafe(json["applyCostPreset"], false);
            bool useCustomSettings = ParseBoolSafe(json["useCustomSettings"], false);
            int feeRatePercent = Math.Max(0, ParseIntSafe(json["feeRatePercent"], 5));
            int shippingCost = Math.Max(0, ParseIntSafe(json["shippingCost"], 210));
            int expenseCost = Math.Max(0, ParseIntSafe(json["expenseCost"], 55));

            string priceMode = json.Value<string>("priceMode") ?? "NONE";
            int priceValue = ParseIntSafe(json["priceValue"], 0);

            int affected = 0;
            var pendingBatchTxtWrites = new List<KeyValuePair<string, string>>();
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
                        pendingBatchTxtWrites.Add(new KeyValuePair<string, string>(txtPath, txtContent));
                    }
                    catch (Exception tex)
                    {
                        Console.WriteLine("一括更新 説明文txt例外: " + tex.Message);
                    }

                    affected++;
                }
            }

            foreach (var tw in pendingBatchTxtWrites)
            {
                try { File.WriteAllText(tw.Key, tw.Value, Encoding.UTF8); } catch { }
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
                if (!IsValidItemStatus(status))
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "不正なステータスです: " + status } });
                    return;
                }
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
                // レビュー 67-2: settingsUpdatedAt は SyncStore 側で単調増加させて保存する
                // (クライアント由来の大きな値が保存済みでも逆行させない)
                SyncStore.SaveDefaultSettingsMonotonic(cfg);
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
                if (!IsValidItemStatus(asStatus))
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "不正なステータスです: " + asStatus } });
                    return;
                }

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

                    int profit = Math.Max(0, totalSoldPrice - newShippingCost - (totalSoldPrice * newFeeRatePercent / 100) - newExpenseCost);

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

                // フェーズ 6 是正 (Build 13 / 49-3): フェイルセーフ — 復元できた子が 0 件で、
                // 構造上もまとめ商品でない場合は、通常商品の誤解除として削除を実行せずエラーにする
                int parentChildCount = parentItem.ContainsKey("mergedChildCount") ? ParseIntSafe(parentItem["mergedChildCount"], 0) : 0;
                if (restoredCount == 0 && parentChildCount <= 0)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "まとめ商品ではないため解除できません" } });
                    return;
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

}
