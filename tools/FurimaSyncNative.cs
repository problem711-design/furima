using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

class FurimaSync
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Furima PC Sync Tool (.NET Framework Native) ===");
        string serverUrl = "";
        string authToken = Environment.GetEnvironmentVariable("FURIMA_AUTH_TOKEN") ?? "";
        if (args.Length > 0)
        {
            serverUrl = args[0];
        }
        else
        {
            Console.Write("スマホアプリのWebサーバーURLを入力してください (例: http://192.168.1.10:8080): ");
            serverUrl = Console.ReadLine();
            if (serverUrl != null) serverUrl = serverUrl.Trim();
        }
        if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
        {
            authToken = args[1].Trim();
        }

        if (string.IsNullOrEmpty(serverUrl))
        {
            Console.WriteLine("URLが入力されませんでした。終了します。");
            return;
        }

        serverUrl = serverUrl.TrimEnd('/');

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string storeJsonPath = Path.Combine(baseDir, "store.json");
        string syncDir = Path.Combine(baseDir, "出品写真_PC保存用");

        if (File.Exists(storeJsonPath))
        {
            try
            {
                string json = File.ReadAllText(storeJsonPath, Encoding.UTF8);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    if (dict.ContainsKey("path") && dict["path"] != null)
                    {
                        string p = dict["path"].ToString();
                        if (!string.IsNullOrEmpty(p)) syncDir = p;
                    }
                    if (string.IsNullOrEmpty(authToken) && dict.ContainsKey("authToken") && dict["authToken"] != null)
                    {
                        string t = dict["authToken"].ToString();
                        if (!string.IsNullOrEmpty(t)) authToken = t.Trim();
                    }
                }
            }
            catch { }
        }

        try
        {
            string browserUrl = serverUrl;
            if (!string.IsNullOrEmpty(authToken) && browserUrl.IndexOf("token=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                browserUrl += (browserUrl.IndexOf('?') >= 0 ? "&" : "?") + "token=" + Uri.EscapeDataString(authToken);
            }
            Console.WriteLine("ブラウザで管理画面を開いています: " + serverUrl);
            Process.Start(new ProcessStartInfo(browserUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ブラウザ自動起動警告: " + ex.Message);
        }

        Directory.CreateDirectory(syncDir);
        Console.WriteLine("同期先フォルダ: " + syncDir);
        Console.WriteLine("30秒ごとに自動同期を開始します。(Ctrl+Cで終了)");

        using (var cts = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            RunSyncLoopAsync(serverUrl, authToken, syncDir, cts.Token).GetAwaiter().GetResult();
        }
    }

    private static async Task RunSyncLoopAsync(string serverUrl, string authToken, string syncDir, CancellationToken ct)
    {
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            if (!string.IsNullOrEmpty(authToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    string apiUrl = serverUrl + "/api/sync/full-data";
                    string json = await client.GetStringAsync(apiUrl).ConfigureAwait(false);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    int count = 0;
                    if (dict != null && dict.ContainsKey("items") && dict["items"] != null)
                    {
                        var itemsList = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(dict["items"].ToString());
                        if (itemsList != null)
                        {
                            foreach (var item in itemsList)
                            {
                                if (item == null) continue;
                                int id = Convert.ToInt32(item["id"]);
                                string title = item.ContainsKey("title") && item["title"] != null ? item["title"].ToString() : "商品";
                                string description = item.ContainsKey("description") && item["description"] != null ? item["description"].ToString() : "";
                                int price = item.ContainsKey("price") ? Convert.ToInt32(item["price"]) : 0;
                                int profit = item.ContainsKey("profit") ? Convert.ToInt32(item["profit"]) : 0;
                                string statusLabel = item.ContainsKey("statusLabel") && item["statusLabel"] != null ? item["statusLabel"].ToString() : "";

                                foreach (char c in Path.GetInvalidFileNameChars())
                                {
                                    title = title.Replace(c, '_');
                                }

                                string itemFolderName = id + "_" + title;
                                string itemDirPath = Path.Combine(syncDir, itemFolderName);
                                Directory.CreateDirectory(itemDirPath);

                                string txtPath = Path.Combine(itemDirPath, "商品情報_説明文.txt");
                                string txtContent = "【商品ID】 " + id + "\n【タイトル】 " + title + "\n【販売価格】 ¥" + price.ToString("N0") + "\n【純利益】 ¥" + profit.ToString("N0") + "\n【ステータス】 " + statusLabel + "\n\n--- 商品説明 ---\n" + description;
                                WriteBytesAtomic(txtPath, Encoding.UTF8.GetBytes(txtContent));

                                if (item.ContainsKey("photos") && item["photos"] != null)
                                {
                                    var photosList = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(item["photos"].ToString());
                                    if (photosList != null)
                                    {
                                        int photoIdx = 1;
                                        foreach (var photo in photosList)
                                        {
                                            if (photo == null) continue;
                                            string targetUrl = ResolvePhotoUrl(serverUrl, photo);
                                            if (string.IsNullOrEmpty(targetUrl)) continue;
                                            try
                                            {
                                                byte[] photoBytes = await client.GetByteArrayAsync(targetUrl).ConfigureAwait(false);
                                                string ext = DetectImageExtension(photoBytes, targetUrl, photo);
                                                string photoFileName = "photo_" + photoIdx.ToString("00") + ext;
                                                string photoPath = Path.Combine(itemDirPath, photoFileName);
                                                WriteBytesAtomic(photoPath, photoBytes);
                                                photoIdx++;
                                            }
                                            catch (Exception photoEx)
                                            {
                                                Console.WriteLine("写真DL失敗 (ID: " + id + ", URL: " + targetUrl + "): " + photoEx.Message);
                                            }
                                        }
                                    }
                                }
                                count++;
                            }
                        }
                    }
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 同期成功: " + count + "件の商品と写真を同期しました。");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 接続エラー: " + ex.Message);
                }

                try
                {
                    await Task.Delay(30000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static string ResolvePhotoUrl(string serverUrl, Dictionary<string, object> photo)
    {
        string photoUrl = photo.ContainsKey("url") && photo["url"] != null ? photo["url"].ToString().Trim() : "";
        string thumbUrl = photo.ContainsKey("thumbUrl") && photo["thumbUrl"] != null ? photo["thumbUrl"].ToString().Trim() : "";
        string targetUrl = !string.IsNullOrEmpty(photoUrl) ? photoUrl : thumbUrl;
        if (string.IsNullOrEmpty(targetUrl)) return "";

        if (targetUrl.StartsWith("/"))
        {
            return serverUrl + targetUrl;
        }

        try
        {
            Uri photoUri = new Uri(targetUrl);
            string host = photoUri.Host.ToLowerInvariant();
            if (host == "127.0.0.1" || host == "0.0.0.0" || host == "localhost" || host == "::1")
            {
                Uri serverUri = new Uri(serverUrl);
                return serverUri.Scheme + "://" + serverUri.Authority + photoUri.PathAndQuery;
            }
        }
        catch
        {
            int apiIdx = targetUrl.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            if (apiIdx >= 0)
            {
                return serverUrl + targetUrl.Substring(apiIdx);
            }
        }
        return targetUrl;
    }

    private static string DetectImageExtension(byte[] bytes, string targetUrl, Dictionary<string, object> photo)
    {
        if (bytes != null && bytes.Length >= 4)
        {
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return ".gif";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return ".webp";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        }
        string candidate = "";
        if (photo.ContainsKey("fileName") && photo["fileName"] != null) candidate = photo["fileName"].ToString();
        else if (!string.IsNullOrEmpty(targetUrl)) candidate = targetUrl.Split('?')[0];
        string ext = "";
        try { ext = Path.GetExtension(candidate).ToLowerInvariant(); } catch { ext = ""; }
        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".gif") return ext;
        return ".jpg";
    }

    private static void WriteBytesAtomic(string targetPath, byte[] data)
    {
        string tmpPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tmpPath, data);
            if (File.Exists(targetPath))
            {
                File.Replace(tmpPath, targetPath, null, true);
            }
            else
            {
                File.Move(tmpPath, targetPath);
            }
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        }
    }
}
