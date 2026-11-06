using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;
using System.Collections.Generic;

class FurimaSync
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Furima PC Sync Tool (.NET Framework Native) ===");
        string serverUrl = "";
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

        if (string.IsNullOrEmpty(serverUrl))
        {
            Console.WriteLine("URLが入力されませんでした。終了します。");
            Console.WriteLine("何かキーを押すと終了します...");
            Console.ReadKey();
            return;
        }

        serverUrl = serverUrl.TrimEnd('/');

        try
        {
            Console.WriteLine("ブラウザで管理画面を開いています: " + serverUrl);
            Process.Start(new ProcessStartInfo(serverUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ブラウザ自動起動警告: " + ex.Message);
        }

        string syncDir = Path.Combine(Directory.GetCurrentDirectory(), "出品写真_PC保存用");
        Directory.CreateDirectory(syncDir);

        Console.WriteLine("同期先フォルダ: " + syncDir);
        Console.WriteLine("30秒ごとに自動同期を開始します。(Ctrl+Cで終了)");

        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(15);

            while (true)
            {
                try
                {
                    string apiUrl = serverUrl + "/api/sync/full-data";
                    var task = client.GetStringAsync(apiUrl);
                    task.Wait();
                    string json = task.Result;

                    var serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = int.MaxValue;
                    var dict = serializer.Deserialize<Dictionary<string, object>>(json);

                    int count = 0;
                    if (dict != null && dict.ContainsKey("items"))
                    {
                        var itemsList = dict["items"] as IEnumerable<object>;
                        if (itemsList != null)
                        {
                            foreach (var itemObj in itemsList)
                            {
                                var item = itemObj as Dictionary<string, object>;
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

                                // 説明文.txt
                                string txtPath = Path.Combine(itemDirPath, "商品情報_説明文.txt");
                                string txtContent = "【商品ID】 " + id + "\n【タイトル】 " + title + "\n【販売価格】 ¥" + price.ToString("N0") + "\n【純利益】 ¥" + profit.ToString("N0") + "\n【ステータス】 " + statusLabel + "\n\n--- 商品説明 ---\n" + description;
                                File.WriteAllText(txtPath, txtContent, Encoding.UTF8);

                                // 写真
                                if (item.ContainsKey("photos") && item["photos"] != null)
                                {
                                    var photosList = item["photos"] as IEnumerable<object>;
                                    if (photosList != null)
                                    {
                                        int photoIdx = 1;
                                        foreach (var photoObj in photosList)
                                        {
                                            var photo = photoObj as Dictionary<string, object>;
                                            if (photo != null)
                                            {
                                                string photoUrl = photo.ContainsKey("url") && photo["url"] != null ? photo["url"].ToString().Trim() : "";
                                                string thumbUrl = photo.ContainsKey("thumbUrl") && photo["thumbUrl"] != null ? photo["thumbUrl"].ToString().Trim() : "";

                                                string targetUrl = !string.IsNullOrEmpty(photoUrl) ? photoUrl : thumbUrl;
                                                if (string.IsNullOrEmpty(targetUrl)) continue;

                                                if (targetUrl.StartsWith("/"))
                                                {
                                                    targetUrl = serverUrl + targetUrl;
                                                }
                                                else
                                                {
                                                    try
                                                    {
                                                        Uri photoUri = new Uri(targetUrl);
                                                        string host = photoUri.Host.ToLower();
                                                        if (host == "127.0.0.1" || host == "0.0.0.0" || host == "localhost" || host == "::1")
                                                        {
                                                            Uri serverUri = new Uri(serverUrl);
                                                            targetUrl = serverUri.Scheme + "://" + serverUri.Authority + photoUri.PathAndQuery;
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        int apiIdx = targetUrl.IndexOf("/api/");
                                                        if (apiIdx >= 0)
                                                        {
                                                            targetUrl = serverUrl + targetUrl.Substring(apiIdx);
                                                        }
                                                    }
                                                }

                                                try
                                                {
                                                    string photoFileName = "photo_" + photoIdx.ToString("00") + ".jpg";
                                                    string photoPath = Path.Combine(itemDirPath, photoFileName);

                                                    var bytesTask = client.GetByteArrayAsync(targetUrl);
                                                    bytesTask.Wait();
                                                    byte[] photoBytes = bytesTask.Result;
                                                    File.WriteAllBytes(photoPath, photoBytes);
                                                    photoIdx++;
                                                }
                                                catch (Exception photoEx)
                                                {
                                                    Console.WriteLine("写真DL失敗 (ID: " + id + ", URL: " + targetUrl + "): " + photoEx.Message);
                                                }
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
                catch (Exception ex)
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 接続エラー: " + ex.Message);
                }

                Thread.Sleep(30000);
            }
        }
    }
}
