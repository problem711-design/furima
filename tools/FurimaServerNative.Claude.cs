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
            { "referenceMd5", job.referenceMd5 ?? "" },
            { "referenceFileName", job.referenceFileName ?? "" },
            { "stampNumber", job.stampNumber },
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
            // フェーズ 6-8 (§35.3): 手打ちプロンプト用の任意フィールド。promptText が指定されたら
            // templateId より優先する。customPromptName は表示名、registerTemplateName は登録試行の指定。
            string promptTextRaw = json.Value<string>("promptText");
            string customPromptName = (json.Value<string>("customPromptName") ?? "").Trim();
            string registerTemplateNameRaw = json.Value<string>("registerTemplateName");
            bool hasPromptText = promptTextRaw != null;
            string promptTextSnapshot = hasPromptText ? promptTextRaw.Trim() : null;
            if (hasPromptText)
            {
                // §35.4 / §40.4: 空白のみは不可、上限は Trim 後の 4,000 文字 (超過は切り捨てずエラー)
                if (promptTextSnapshot.Length == 0)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "プロンプトが入力されていません" } });
                    return;
                }
                if (promptTextSnapshot.Length > 4000)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "プロンプトが長すぎます (4,000 文字以内で入力してください)" } });
                    return;
                }
            }
            // §40.4: 手打ちジョブの表示名フォールバック (customPromptName → registerTemplateName → 既定名)
            string registerTemplateNameTrimmed = (registerTemplateNameRaw ?? "").Trim();
            string freeTextJobName = !string.IsNullOrEmpty(customPromptName) ? customPromptName
                : (!string.IsNullOrEmpty(registerTemplateNameTrimmed) ? registerTemplateNameTrimmed : "手打ちプロンプト");
            string origin = (json.Value<string>("origin") ?? "").Trim();
            if (origin != "pc" && origin != "phone") origin = "pc";
            string queue = (json.Value<string>("queue") ?? "").Trim();
            if (queue != "patrol" && queue != "manual" && queue != "waiting") queue = "patrol";
            // フェーズ 6-13 (§47.10-5): 参考画像の添付 (任意・1 枚)。検証は商品解決後にロック内で行う。
            string referenceMd5 = (json.Value<string>("referenceMd5") ?? "").Trim().ToLowerInvariant();
            string referenceFileName = (json.Value<string>("referenceFileName") ?? "").Trim();
            // フェーズ 6-15 (§50.2-2 / §94.2-1): 番号スタンプ。未指定・真偽値でない場合は true に正規化する
            bool stampNumber = true;
            var stampTok = json["stampNumber"];
            if (stampTok != null && stampTok.Type == JTokenType.Boolean)
            {
                stampNumber = stampTok.Value<bool>();
            }

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
            if (!hasPromptText && string.IsNullOrEmpty(templateId))
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
                        if (!hasPromptText)
                        {
                            var settings = SyncStore.GetDefaultSettings();
                            if (settings != null && settings.promptTemplates != null)
                            {
                                tpl = settings.promptTemplates.Find(t => t != null && t.id == templateId);
                            }
                        }
                        if (!hasPromptText && tpl == null)
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
                            else if (!string.IsNullOrEmpty(referenceMd5) && !IsReferenceAttachable(referenceMd5))
                            {
                                // §47.10-5 / §47.11-4: 形式・コレクション登録済み・実ファイル存在を満たさない参考画像は作成エラーにする
                                errorMessage = "参考画像が見つかりません (未登録または実ファイルがありません)";
                            }
                            else
                            {
                                string folderName = "";
                                string dir = FindExistingItemFolder(resolvedItemId);
                                if (!string.IsNullOrEmpty(dir)) folderName = Path.GetFileName(dir);
                                var refEntry = !string.IsNullOrEmpty(referenceMd5) ? ReferencePhotoStore.GetEntry(referenceMd5) : null;
                                string refNameSnapshot = !string.IsNullOrEmpty(referenceFileName) ? referenceFileName : (refEntry != null ? (refEntry.displayName ?? "") : "");
                                var job = new ClaudeJob
                                {
                                    itemId = resolvedItemId,
                                    itemUuid = item.ContainsKey("syncUuid") && item["syncUuid"] != null ? item["syncUuid"].ToString().Trim() : itemUuid,
                                    itemTitle = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "",
                                    folderName = folderName,
                                    photos = jobPhotos,
                                    templateId = hasPromptText ? "" : tpl.id,
                                    templateName = hasPromptText ? freeTextJobName : (tpl.name ?? ""),
                                    promptText = hasPromptText ? promptTextSnapshot : (tpl.content ?? ""),
                                    origin = origin,
                                    queue = queue,
                                    referenceMd5 = referenceMd5,
                                    referenceFileName = refNameSnapshot,
                                    stampNumber = stampNumber
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

            // フェーズ 6-8 (§35.4): テンプレート登録はジョブ作成の成功後に同一リクエスト内で試みる。
            // 登録の成否はジョブ作成と切り離し、失敗してもジョブは作成されたものとして応答する。
            bool templateRegistered = false;
            string templateRegisterError = null;
            if (hasPromptText && registerTemplateNameRaw != null)
            {
                string regError;
                if (SyncStore.TryRegisterPromptTemplate(registerTemplateNameRaw, promptTextSnapshot, out regError))
                {
                    templateRegistered = true;
                    BumpSettingsRevision();
                }
                else
                {
                    templateRegisterError = regError;
                }
            }

            // リビジョン加算は CreateJob が単一ロック内で行う (フェーズ 6-1a)。
            // ここで二重に加算しないこと。
            var createResponse = new Dictionary<string, object>
            {
                { "status", "ok" },
                { "job", ToPublicClaudeJob(created) },
                { "claudeRevision", GetClaudeRevision() },
                { "templateRegistered", templateRegistered }
            };
            if (templateRegisterError != null) createResponse["templateRegisterError"] = templateRegisterError;
            SendJson(res, createResponse);
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
                long pubExpiry = job.jobTokenExpiresAt > 0
                    ? job.jobTokenExpiresAt
                    : (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30L * 60L * 1000L);
                pub["jobToken"] = job.jobToken;
                pub["jobTokenExpiresAt"] = pubExpiry;
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

    // v1.6 (Build 1) P3 案B: approval_pending の 10 分タイムアウトによる自動待機落ちを廃止。
    // 承認・拒否・取消・手動での待機移動まで承認待ちを維持する。
    internal static bool ApplyApprovalPendingExpiry(ClaudeJob job)
    {
        return false;
    }

    // v1.6 (Build 1) P3 案B + processing 完全延長なしガード:
    // - approved (未着手・トークン消費前) はタイムアウトによる自動待機落ちを廃止し、失効させない。
    // - processing (写真取得済み・トークン消費開始済み) のみを対象に期限切れを評価し、
    //   起動時延長は行わず、結果/エラー 0 件であっても絶対に waiting へ戻さず done / done_with_errors で終端停止する。
    internal static bool ApplyTokenExpiry(ClaudeJob job)
    {
        if (job == null) return false;
        if (job.status != "processing") return false;
        if (string.IsNullOrEmpty(job.jobToken) || job.jobTokenExpiresAt <= 0) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= job.jobTokenExpiresAt) return false;
        bool changed = false;
        // フェーズ 6 是正 (Build 13 / 49-2): 終端時は完了処理と同様に自動エラー解消 (まとめ範囲) を効かせる
        var expiryBundleUuids = CollectBundleUuidSetForItemUuid(job.itemUuid);
        ClaudeJobStore.MutateJobWithErrorSupersede(job.jobId, j =>
        {
            // ロック内でも有効期限を再検証する (結果投稿による延長と競合しても誤失効させない)
            if (j.status == "processing"
                && !string.IsNullOrEmpty(j.jobToken) && j.jobTokenExpiresAt > 0
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > j.jobTokenExpiresAt)
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
                j.jobToken = null;
                j.jobTokenExpiresAt = 0;
                changed = true;
                return true;
            }
            return false;
        }, expiryBundleUuids);
        if (changed)
        {
            BumpClaudeRevision();
            // フェーズ 6-13 (§47.11-4): トークン失効で終端へ落ちたら参考画像の回収も試みる
            SweepReferenceOrphans();
        }
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
            bool applied = false;
            ClaudeJobStore.MutateJob(jobId, j =>
            {
                // フェーズ 6 是正 (Build 13 / 49-2): ロック内でも前提状態を再検証する
                if (j.status != "queued" || j.queue == "waiting") return false;
                j.status = "approval_pending";
                j.approvalRequestedAt = now;
                j.jobToken = null;
                j.jobTokenExpiresAt = 0;
                applied = true;
                return true;
            });
            if (!applied)
            {
                res.StatusCode = 409;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブの状態が変わったため、承認リクエストできませんでした" } });
                return;
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
            bool applied = false;
            if (approved)
            {
                // jobToken は権限分離ではなく「スマホ承認が完了しない限り実行APIが通らない」
                // セーフティロックとして発行する (レビュー指摘8-2)。v1.6 (Build 1) 案B により
                // 未着手 (approved) の間は失効させず (jobTokenExpiresAt=0)、処理開始 (processing) 時に 30 分期限を設定する。
                string newToken = Guid.NewGuid().ToString();
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    // フェーズ 6 是正 (Build 13 / 49-2): ロック内でも承認待ちであることを再検証する。
                    // 取消・待機戻しと交錯した場合に、終端済みジョブへトークンを発行して復活させない。
                    if (j.status != "approval_pending") return false;
                    j.status = "approved";
                    j.jobToken = newToken;
                    j.jobTokenExpiresAt = 0;
                    applied = true;
                    return true;
                });
            }
            else
            {
                ClaudeJobStore.MutateJob(jobId, j =>
                {
                    if (j.status != "approval_pending") return false;
                    j.status = "denied";
                    j.completedAt = now;
                    j.jobToken = null;
                    j.jobTokenExpiresAt = 0;
                    applied = true;
                    return true;
                });
            }
            if (!applied)
            {
                res.StatusCode = 409;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブの状態が変わったため、承認を反映できませんでした" } });
                return;
            }
            BumpClaudeRevision();
            // フェーズ 6-13 (§47.11-4): 拒否で終端へ落ちたら参考画像の回収も試みる
            if (!approved) SweepReferenceOrphans();
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

    // フェーズ 6-10 (§40.3 / §41.3): 戻り値は「ジョブが実行可能な状態 (approved / processing)
    // にあるか」。変更ラムダ内でも状態ガードを行い、それ以外 (取消済み・終端など) には記録しない。
    // 呼び出し側は、false の場合に「実行可能な状態ではない」側の応答へ切り替える。
    // レビュー 64-3: 同一写真のエラーが既に記録済み (重複) の場合も、状態が実行可能なら
    // true を返す (再保存はしない)。新規追加のときだけ保存とリビジョン加算を行う。
    private static bool RecordClaudeJobPhotoError(string jobId, string md5, string message)
    {
        bool added = false;
        bool executable = false;
        ClaudeJobStore.MutateJob(jobId, j =>
        {
            if (j.status != "approved" && j.status != "processing") return false;
            executable = true;
            if (j.errors == null) j.errors = new List<ClaudeJobError>();
            foreach (var e in j.errors)
            {
                if (string.Equals(e.sourceMd5, md5, StringComparison.OrdinalIgnoreCase)) return false;
            }
            j.errors.Add(new ClaudeJobError { sourceMd5 = md5, message = message });
            // Build 13 (13-3): 実行が始まったジョブは取り消せないよう、初回エラー記録でも processing へ進める
            if (j.status == "approved")
            {
                j.status = "processing";
                j.jobTokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 30L * 60L * 1000L;
            }
            added = true;
            return true;
        });
        if (added) BumpClaudeRevision();
        return executable;
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
        SweepReferenceOrphans();
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
            bool applied = false;
            ClaudeJobStore.MutateJob(jobId, j =>
            {
                // フェーズ 6 是正 (Build 13 / 49-2): ロック内でも移動可能な状態かを再検証する。
                // 承認が先行したジョブ (approved + トークン発行済み) を巻き戻さない。
                if (j.status != "queued" && j.status != "approval_pending") return false;
                j.queue = target;
                if (target == "waiting")
                {
                    j.status = "queued";
                    j.approvalRequestedAt = 0;
                    j.jobToken = null;
                    j.jobTokenExpiresAt = 0;
                }
                else if (j.status == "queued")
                {
                    j.status = "approval_pending";
                    j.approvalRequestedAt = now;
                }
                applied = true;
                return true;
            });
            if (!applied)
            {
                res.StatusCode = 409;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブの状態が変わったため、移動できませんでした" } });
                return;
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
        // フェーズ 6-10 (§41.3): CancelJob の前に承認・トークンの失効評価を通す。
        // 失効で終端へ落ちたジョブは取消対象外となり、失効の結果 (待機戻し・失効エラー記録) を
        // 取消で上書きしない。待機へ戻っただけのジョブは queued として通常どおり取消できる。
        var before = ClaudeJobStore.GetJob(jobId);
        if (before != null)
        {
            ApplyApprovalPendingExpiry(before);
            ApplyTokenExpiry(before);
        }
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
        SweepReferenceOrphans();
        var job = ClaudeJobStore.GetJob(jobId);
        SendJson(res, new Dictionary<string, object>
        {
            { "status", "ok" },
            { "job", ToPublicClaudeJob(job) },
            { "claudeRevision", GetClaudeRevision() }
        });
    }

    // P2: ダッシュボードから巡回の合図 (claudeRevision) だけを出し直す。
    // PC ローカル経路かつ Bearer 必須。ジョブ状態は一切変更しない。
    private static void HandleWakeClaudePatrol(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            IPAddress remoteIp = req != null && req.RemoteEndPoint != null ? req.RemoteEndPoint.Address : null;
            if (req == null || HasForwardedHeaders(req) || remoteIp == null || !IPAddress.IsLoopback(remoteIp))
            {
                res.StatusCode = 403;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "この操作は PC ローカルからのみ実行できます" } });
                return;
            }
            string authHeader = (req.Headers["Authorization"] ?? "").Trim();
            string expectedAuth = "Bearer " + authToken;
            if (!string.Equals(authHeader, expectedAuth, StringComparison.Ordinal))
            {
                res.StatusCode = 401;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "401 Unauthorized: Invalid or missing Bearer token" } });
                return;
            }
            long rev = BumpClaudeRevision();
            var jobs = ClaudeJobStore.GetJobs(null);
            int processing = 0;
            int approved = 0;
            int approvalPending = 0;
            int waiting = 0;
            int errors = 0;
            foreach (var j in jobs)
            {
                if (j == null) continue;
                if (j.status == "processing") processing++;
                else if (j.status == "approved") approved++;
                else if (j.status == "approval_pending") approvalPending++;
                else if (j.status == "queued" && j.queue == "waiting") waiting++;
                if (j.status == "done_with_errors" && !j.errorDismissed) errors++;
            }
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "claudeRevision", rev },
                { "counts", new Dictionary<string, object>
                    {
                        { "processing", processing },
                        { "approved", approved },
                        { "approvalPending", approvalPending },
                        { "waiting", waiting },
                        { "error", errors }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleWakeClaudePatrol エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }
}
