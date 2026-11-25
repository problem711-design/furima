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
    // ===== フェーズ 6-13 (§47.10): 参考画像コレクション =====
    // 実ファイルは商品写真ストアと完全分離した専用ディレクトリに置き (ReferencePhotoStore)、
    // 取り込み箱は Move-in 方式、配信は専用ルート、ワーカー取得はジョブ専用ルートに分離する。

    private static bool IsReferenceAttachable(string md5)
    {
        if (!ReferencePhotoStore.IsValidMd5(md5)) return false;
        if (!ReferencePhotoStore.IsRegistered(md5)) return false;
        return ReferencePhotoStore.ResolveFilePath(md5) != null;
    }

    private static HashSet<string> CollectUnfinishedReferenceMd5s()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = ClaudeJobStore.GetJobs(null);
        foreach (var j in jobs)
        {
            if (j == null) continue;
            if (j.status != "queued" && j.status != "approval_pending" && j.status != "approved" && j.status != "processing") continue;
            string md5 = (j.referenceMd5 ?? "").Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(md5)) set.Add(md5);
        }
        return set;
    }

    private static bool IsReferenceInUse(string md5)
    {
        return CollectUnfinishedReferenceMd5s().Contains(md5);
    }

    // 登録解除済み・未参照の実ファイルを回収する。ジョブの終端遷移 (完了・取消・拒否・
    // トークン失効) とサーバー起動時から呼ぶ (§47.10-5 / §47.11-4)。
    private static void SweepReferenceOrphans()
    {
        try
        {
            ReferencePhotoStore.SweepOrphans(CollectUnfinishedReferenceMd5s());
        }
        catch (Exception ex)
        {
            Console.WriteLine("SweepReferenceOrphans エラー: " + ex.Message);
        }
    }

    private static Dictionary<string, object> ReferenceEntryToPublic(ClaudeReferenceEntry e)
    {
        return new Dictionary<string, object>
        {
            { "md5", e != null ? (e.md5 ?? "") : "" },
            { "displayName", e != null ? (e.displayName ?? "") : "" },
            { "fileName", e != null ? (e.fileName ?? "") : "" },
            { "addedAt", e != null ? e.addedAt : 0 }
        };
    }

    // 取り込み箱の走査 (Move-in 方式 §47.10-2)。戻り値は箱に残っている未対応形式のファイル名一覧。
    private static List<string> ScanReferenceInbox()
    {
        var unsupported = new List<string>();
        try
        {
            ReferencePhotoStore.EnsureDirs();
            string inbox = ReferencePhotoStore.GetInboxDir();
            string photosDir = ReferencePhotoStore.GetPhotosDir();
            if (string.IsNullOrEmpty(inbox) || !Directory.Exists(inbox)) return unsupported;
            string[] files = Directory.GetFiles(inbox);
            foreach (string path in files)
            {
                string fileName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(fileName)) continue;
                if (fileName.StartsWith(".") || fileName.StartsWith("~")) continue;
                // §92.3-2: 対象外拡張子はファイルを開かず未対応へ回す (ポーリング毎の全量読込を避ける)
                string extLower = Path.GetExtension(fileName).ToLowerInvariant();
                if (extLower != ".jpg" && extLower != ".jpeg" && extLower != ".png")
                {
                    unsupported.Add(fileName);
                    continue;
                }
                byte[] raw;
                try
                {
                    raw = File.ReadAllBytes(path);
                }
                catch (Exception)
                {
                    // コピー中などでロックされている場合はその回をスキップし、次回走査に回す (§47.11-2)
                    continue;
                }
                if (raw == null || raw.Length == 0) continue;
                string mime = DetectImageMimeType(raw);
                if (mime != "image/jpeg" && mime != "image/png")
                {
                    // 未対応形式は移動・削除せず箱に残し、未対応として表示する (§47.11-2)
                    unsupported.Add(fileName);
                    continue;
                }
                string md5;
                using (var md5Alg = MD5.Create())
                {
                    md5 = BitConverter.ToString(md5Alg.ComputeHash(raw)).Replace("-", "").ToLower();
                }
                if (ReferencePhotoStore.IsRegistered(md5))
                {
                    // 重複投入: 既存の登録 (表示名等) を維持し、重複側の箱ファイルだけを掃除する (§47.11-2)
                    try { File.Delete(path); } catch (Exception) { }
                    continue;
                }
                string destName = md5 + (mime == "image/png" ? ".png" : ".jpg");
                string dest = Path.Combine(photosDir, destName);
                try
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(path, dest);
                }
                catch (Exception)
                {
                    // 移動に失敗したら元ファイルを残して次回走査に回す
                    continue;
                }
                ReferencePhotoStore.TryRegister(md5, fileName, destName);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ScanReferenceInbox エラー: " + ex.Message);
        }
        return unsupported;
    }

    private static void HandleGetClaudeReferences(HttpListenerResponse res)
    {
        try
        {
            var unsupported = ScanReferenceInbox();
            var entries = ReferencePhotoStore.GetEntries();
            var list = new List<Dictionary<string, object>>();
            foreach (var e in entries) list.Add(ReferenceEntryToPublic(e));
            SendJson(res, new Dictionary<string, object>
            {
                { "status", "ok" },
                { "references", list },
                { "count", list.Count },
                { "inboxPath", ReferencePhotoStore.GetInboxDir() },
                { "storePath", ReferencePhotoStore.GetPhotosDir() },
                { "unsupportedFiles", unsupported }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleGetClaudeReferences エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleRegisterReferenceFromItem(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            JObject json = JObject.Parse(jsonStr);
            int itemId = ParseIntSafe(json["itemId"], 0);
            string md5 = (json.Value<string>("md5Hash") ?? "").Trim().ToLowerInvariant();
            if (!ReferencePhotoStore.IsValidMd5(md5))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "参考画像にできる写真ではありません (MD5未確定)" } });
                return;
            }
            var existing = ReferencePhotoStore.GetEntry(md5);
            if (existing != null)
            {
                SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "alreadyRegistered", true }, { "reference", ReferenceEntryToPublic(existing) } });
                return;
            }
            string sourcePath = null;
            string photoFileName = "";
            lock (lockObj)
            {
                if (hashToFilePathMap.ContainsKey(md5)) sourcePath = hashToFilePathMap[md5];
                var item = itemsDb.Find(i => i != null && i.ContainsKey("id") && ParseIntSafe(i["id"], 0) == itemId);
                if (item != null && item.ContainsKey("photos") && item["photos"] != null)
                {
                    var plist = SyncStore.NormalizePhotosList(item["photos"]);
                    foreach (var ph in plist)
                    {
                        if (ph == null) continue;
                        string h = ph.ContainsKey("md5Hash") && ph["md5Hash"] != null ? ph["md5Hash"].ToString().Trim().ToLowerInvariant() : "";
                        if (h == md5)
                        {
                            photoFileName = ph.ContainsKey("fileName") && ph["fileName"] != null ? ph["fileName"].ToString().Trim() : "";
                            break;
                        }
                    }
                }
            }
            // §47.11-3: 実体が欠損している写真からは空の登録を作らずエラーにする
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "写真ファイルが見つかりません" } });
                return;
            }
            byte[] raw = File.ReadAllBytes(sourcePath);
            string mime = DetectImageMimeType(raw);
            if (mime != "image/jpeg" && mime != "image/png")
            {
                res.StatusCode = 415;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "未対応の画像形式です (" + mime + ")" } });
                return;
            }
            ReferencePhotoStore.EnsureDirs();
            string destName = md5 + (mime == "image/png" ? ".png" : ".jpg");
            string dest = Path.Combine(ReferencePhotoStore.GetPhotosDir(), destName);
            if (!File.Exists(dest)) File.Copy(sourcePath, dest);
            string displayName = !string.IsNullOrEmpty(photoFileName) ? photoFileName : Path.GetFileName(sourcePath);
            ReferencePhotoStore.TryRegister(md5, displayName, destName);
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "alreadyRegistered", false }, { "reference", ReferenceEntryToPublic(ReferencePhotoStore.GetEntry(md5)) } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleRegisterReferenceFromItem エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleDeleteClaudeReference(HttpListenerResponse res, string md5)
    {
        try
        {
            if (!ReferencePhotoStore.IsValidMd5(md5))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid md5" } });
                return;
            }
            bool removed = ReferencePhotoStore.Unregister(md5);
            bool retained = false;
            if (removed)
            {
                // 未完了ジョブが参照中なら実ファイルは残し、参照が切れた時点で回収する (§47.10-5)
                if (IsReferenceInUse(md5))
                {
                    retained = true;
                }
                else
                {
                    ReferencePhotoStore.DeleteFileAndThumb(md5);
                }
            }
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "removed", removed }, { "fileRetained", retained } });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleDeleteClaudeReference エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void HandleOpenReferenceFolder(HttpListenerResponse res)
    {
        try
        {
            ReferencePhotoStore.EnsureDirs();
            string targetDir = ReferencePhotoStore.GetInboxDir();
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
                { "folderPath", targetDir },
                { "storePath", ReferencePhotoStore.GetPhotosDir() }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("HandleOpenReferenceFolder エラー: " + ex.Message);
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }

    private static void ServeReferenceOriginal(HttpListenerResponse res, string md5)
    {
        try
        {
            if (!ReferencePhotoStore.IsValidMd5(md5))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid md5" } });
                return;
            }
            string path = ReferencePhotoStore.ResolveFilePath(md5);
            if (string.IsNullOrEmpty(path))
            {
                res.StatusCode = 404;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "参考画像が見つかりません" } });
                return;
            }
            byte[] bytes = File.ReadAllBytes(path);
            string mime = DetectImageMimeType(bytes);
            res.ContentType = string.IsNullOrEmpty(mime) ? "application/octet-stream" : mime;
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("ServeReferenceOriginal エラー: " + ex.Message);
            try
            {
                res.StatusCode = 500;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
            }
            catch (Exception) { }
        }
    }

    // §47.11-3: サムネイル生成にも配信コピーと同じ EXIF 正立化と PNG 透過部の白塗りを適用する
    private static bool TryBuildReferenceThumb(byte[] raw, out byte[] outBytes)
    {
        outBytes = null;
        string mime = DetectImageMimeType(raw);
        if (mime != "image/jpeg" && mime != "image/png") return false;
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

                if (orientation != 1)
                {
                    img.RotateFlip(MapExifOrientationToRotateFlip(orientation));
                    try { img.RemovePropertyItem(0x0112); } catch { }
                }
                int w = img.Width;
                int h = img.Height;
                int le = Math.Max(w, h);
                double scale = le > 320 ? (double)320 / le : 1.0;
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
                    if (jpegCodec == null) return false;
                    using (var encParams = new EncoderParameters(1))
                    {
                        encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                        using (var outMs = new MemoryStream())
                        {
                            bmp.Save(outMs, jpegCodec, encParams);
                            outBytes = outMs.ToArray();
                        }
                    }
                }
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ServeReferenceThumb(HttpListenerResponse res, string md5)
    {
        try
        {
            if (!ReferencePhotoStore.IsValidMd5(md5))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "Invalid md5" } });
                return;
            }
            ReferencePhotoStore.EnsureDirs();
            string thumbPath = Path.Combine(ReferencePhotoStore.GetThumbsDir(), md5 + ".jpg");
            byte[] bytes = null;
            if (File.Exists(thumbPath))
            {
                // キャッシュ読み込みに失敗 (競合・破損) したら再生成へフォールバックする
                try { bytes = File.ReadAllBytes(thumbPath); } catch (Exception) { bytes = null; }
            }
            if (bytes == null)
            {
                string src = ReferencePhotoStore.ResolveFilePath(md5);
                if (string.IsNullOrEmpty(src))
                {
                    res.StatusCode = 404;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "参考画像が見つかりません" } });
                    return;
                }
                byte[] raw = File.ReadAllBytes(src);
                byte[] thumb;
                if (!TryBuildReferenceThumb(raw, out thumb))
                {
                    res.StatusCode = 415;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "サムネイルを生成できない画像形式です" } });
                    return;
                }
                // §92.2-3: 生成したバイト列はメモリからそのまま配信する。キャッシュ保存は
                // 一時ファイル経由のベストエフォートとし、失敗しても 500 にしない
                bytes = thumb;
                try
                {
                    AtomicFileWriter.WriteBytesAtomic(thumbPath, thumb, false);
                }
                catch (Exception) { }
            }
            res.ContentType = "image/jpeg";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("ServeReferenceThumb エラー: " + ex.Message);
            try
            {
                res.StatusCode = 500;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
            }
            catch (Exception) { }
        }
    }
}
