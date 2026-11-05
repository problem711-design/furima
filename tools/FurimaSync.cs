using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Furima PC Sync Tool ===");
        string serverUrl = "";
        if (args.Length > 0)
        {
            serverUrl = args[0];
        }
        else
        {
            Console.Write("スマホアプリのWebサーバーURLを入力してください (例: http://192.168.1.10:8080): ");
            serverUrl = Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(serverUrl))
        {
            Console.WriteLine("URLが入力されませんでした。終了します。");
            return;
        }

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        // ブラウザ自動起動
        try
        {
            Console.WriteLine($"ブラウザで管理画面を開いています: {serverUrl}");
            Process.Start(new ProcessStartInfo(serverUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ブラウザの自動起動に失敗しました: {ex.Message}");
        }

        string syncDir = Path.Combine(Directory.GetCurrentDirectory(), "出品写真_PC保存用");
        Directory.CreateDirectory(syncDir);

        Console.WriteLine($"同期先フォルダ: {syncDir}");
        Console.WriteLine("30秒ごとに自動同期を開始します。(Ctrl+Cで終了)");

        while (true)
        {
            try
            {
                string apiUrl = serverUrl.TrimEnd('/') + "/api/sync/full-data";
                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var items = root.GetProperty("items");

                    int count = 0;
                    foreach (var item in items.EnumerateArray())
                    {
                        int id = item.GetProperty("id").GetInt32();
                        string title = item.GetProperty("title").GetString() ?? "商品";
                        string description = item.GetProperty("description").GetString() ?? "";
                        int price = item.GetProperty("price").GetInt32();
                        int profit = item.GetProperty("profit").GetInt32();
                        string statusLabel = item.GetProperty("statusLabel").GetString() ?? "";

                        // フォルダ名不正文字除去
                        foreach (char c in Path.GetInvalidFileNameChars())
                        {
                            title = title.Replace(c, '_');
                        }
                        string itemFolderName = $"{id}_{title}";
                        string itemDirPath = Path.Combine(syncDir, itemFolderName);
                        Directory.CreateDirectory(itemDirPath);

                        // 説明文.txt出力
                        string txtPath = Path.Combine(itemDirPath, "商品情報_説明文.txt");
                        string txtContent = $"【商品ID】 {id}\n【タイトル】 {title}\n【販売価格】 ¥{price:N0}\n【純利益】 ¥{profit:N0}\n【ステータス】 {statusLabel}\n\n--- 商品説明 ---\n{description}";
                        await File.WriteAllTextAsync(txtPath, txtContent);

                        // 写真ダウンロード
                        if (item.TryGetProperty("photos", out var photos) && photos.ValueKind == JsonValueKind.Array)
                        {
                            int photoIdx = 1;
                            foreach (var photo in photos.EnumerateArray())
                            {
                                string photoUrl = photo.GetProperty("url").GetString();
                                if (!string.IsNullOrEmpty(photoUrl))
                                {
                                    try
                                    {
                                        var photoBytes = await client.GetByteArrayAsync(photoUrl);
                                        string photoFileName = $"photo_{photoIdx:D2}.jpg";
                                        string photoPath = Path.Combine(itemDirPath, photoFileName);
                                        // 既存ファイルとサイズが違う場合のみ書き込み (または常時上書き)
                                        await File.WriteAllBytesAsync(photoPath, photoBytes);
                                        photoIdx++;
                                    }
                                    catch (Exception photoEx)
                                    {
                                        Console.WriteLine($"写真ダウンロード失敗 (ID: {id}, URL: {photoUrl}): {photoEx.Message}");
                                    }
                                }
                            }
                        }
                        count++;
                    }
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 同期成功: {count}件の商品と写真・説明文を同期しました。");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] サーバー応答エラー: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 接続エラー: {ex.Message}");
            }

            await Task.Delay(30000);
        }
    }
}
