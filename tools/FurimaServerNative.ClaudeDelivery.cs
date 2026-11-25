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

    // ワーカー専用取得 (§47.10-5): 商品写真の取得処理とは分離し、状態遷移やエラー記録は持ち込まない。
    // ジョブトークンの検証と approved / processing の状態でのみ許可する。
    private static void HandleGetClaudeJobReference(HttpListenerRequest req, HttpListenerResponse res, string jobId)
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
            string refMd5 = (job.referenceMd5 ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(refMd5))
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "このジョブには参考画像が添付されていません" } });
                return;
            }
            string path = ReferencePhotoStore.ResolveFilePath(refMd5);
            if (string.IsNullOrEmpty(path))
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "参考画像ファイルが見つかりません" } });
                return;
            }
            byte[] raw = File.ReadAllBytes(path);
            byte[] outBytes;
            string outMime;
            string copyError;
            if (!TryBuildClaudeDeliveryCopy(raw, out outBytes, out outMime, out copyError))
            {
                res.StatusCode = 415;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", copyError } });
                return;
            }
            res.ContentType = outMime;
            res.ContentLength64 = outBytes.Length;
            res.OutputStream.Write(outBytes, 0, outBytes.Length);
            res.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleGetClaudeJobReference エラー: " + ex.Message);
            try
            {
                res.StatusCode = 500;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
            }
            catch (Exception) { }
        }
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
                // フェーズ 6-10 (§41.3): 記録が状態ガードで弾かれた場合は、404 ではなく
                // 「実行可能な状態ではない」側の応答を返す (取消済みジョブ等にエラーを残さない)
                if (!RecordClaudeJobPhotoError(job.jobId, md5, "写真ファイルが見つかりません"))
                {
                    var cur = ClaudeJobStore.GetJob(jobId);
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが実行可能な状態ではありません (status: " + (cur != null ? cur.status : "unknown") + ")" } });
                    return;
                }
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
                // フェーズ 6-10 (§41.3): 記録が弾かれた場合は 415 ではなく「実行可能な状態ではない」側の応答
                if (!RecordClaudeJobPhotoError(job.jobId, md5, copyError))
                {
                    var cur = ClaudeJobStore.GetJob(jobId);
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが実行可能な状態ではありません (status: " + (cur != null ? cur.status : "unknown") + ")" } });
                    return;
                }
                res.StatusCode = 415;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", copyError } });
                return;
            }

            // フェーズ 6-10 (§40.3 / 53-2): 確定直前に claudeResultWriteLock の下で状態・トークンを
            // 再検証してから状態遷移と応答を行う。重い読み込み・変換は上のロック外で済ませてある。
            // 再検証で無効ならバイト列を返さず、エラー記録もしない (取消・失効との競合をここで閉じる)。
            lock (claudeResultWriteLock)
            {
                var fresh = ClaudeJobStore.GetJob(jobId);
                if (fresh != null)
                {
                    // レビュー 64-4: 失効評価も他の結果系ハンドラと同様に、ロック内の fresh に対して行う
                    ApplyApprovalPendingExpiry(fresh);
                    ApplyTokenExpiry(fresh);
                    fresh = ClaudeJobStore.GetJob(jobId);
                }
                if (fresh == null)
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが見つかりません" } });
                    return;
                }
                string freshToken = req.Headers["X-Claude-Job-Token"];
                if (string.IsNullOrEmpty(fresh.jobToken) || string.IsNullOrEmpty(freshToken) ||
                    !string.Equals(freshToken, fresh.jobToken, StringComparison.Ordinal))
                {
                    res.StatusCode = 403;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブトークンが無効です（未承認・期限切れ・不一致）" } });
                    return;
                }
                if (fresh.status != "approved" && fresh.status != "processing")
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "ジョブが実行可能な状態ではありません (status: " + fresh.status + ")" } });
                    return;
                }

                if (fresh.status == "approved")
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
}
