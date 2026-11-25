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
    // レビュー 24-1: トンネル (cloudflared) 経由のリクエストには転送ヘッダが付く。
    // ループバック免除と管理画面の配信を「直接のローカルアクセス」だけに限定するための判定。
    private static bool HasForwardedHeaders(HttpListenerRequest req)
    {
        return !string.IsNullOrEmpty(req.Headers["Cf-Connecting-Ip"]) ||
               !string.IsNullOrEmpty(req.Headers["Cf-Ray"]) ||
               !string.IsNullOrEmpty(req.Headers["X-Forwarded-For"]) ||
               !string.IsNullOrEmpty(req.Headers["Forwarded"]) ||
               !string.IsNullOrEmpty(req.Headers["X-Real-IP"]);
    }

    private const int MaxPhotoUploadBytes = 50 * 1024 * 1024; // 50 MB

    private static bool TryReadStreamWithLimit(Stream input, int maxBytes, out byte[] bytes, out bool tooLarge)
    {
        bytes = new byte[0];
        tooLarge = false;
        if (input == null) return false;
        using (var ms = new MemoryStream())
        {
            byte[] buffer = new byte[81920];
            int total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    tooLarge = true;
                    return false;
                }
                ms.Write(buffer, 0, read);
            }
            bytes = ms.ToArray();
            return true;
        }
    }

    private static bool AuthenticateRequest(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;

        if (req.HttpMethod == "OPTIONS") return true;

        IPAddress remoteIp = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address : null;
        if (remoteIp == null)
        {
            res.StatusCode = 403;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "403 Forbidden: Missing remote endpoint" } });
            return false;
        }
        bool isLoopback = IPAddress.IsLoopback(remoteIp);
        bool forwarded = HasForwardedHeaders(req);

        string path = req.Url != null ? req.Url.AbsolutePath : "/";

        // レビュー 24-1 (方式1): 管理画面 HTML は合鍵そのものを含むため PC ローカル専用とする。
        // 転送ヘッダ付き (トンネル経由) またはループバック以外からの画面取得は拒否する。
        if (path == "/")
        {
            if (forwarded || !isLoopback)
            {
                res.StatusCode = 403;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "管理画面は PC ローカルからのみ利用できます" } });
                return false;
            }
            return true;
        }

        // ループバックからの直接アクセスは許可 (トンネル経由は転送ヘッダで除外する)
        if (isLoopback && !forwarded) return true;
        // フェーズ 6-3: 変化カウンタの公開窓口のみ認証を免除する (GET + 完全一致パスのみ)。
        // 見張りスクリプトはこの数字だけを見て、認証情報を持たない。
        if (req.HttpMethod == "GET" && path == "/api/claude/revision")
        {
            return true;
        }

        // フェーズ 6-3 (案2): 許可アドレス (Muse 端末) からの対象エンドポイントだけ Bearer を免除する。
        // 免除するのは Bearer 層のみで、ジョブトークン等の検証は各ハンドラで従来どおり行う。
        if (IsTrustedClaudeExemptRequest(req, path))
        {
            RecordClaudePatrolContact();
            return true;
        }

        // ループバック以外からの /api/ リクエストには Bearer トークンが必要
        if (path.StartsWith("/api/"))
        {
            string authHeader = req.Headers["Authorization"] ?? "";
            string expectedAuth = "Bearer " + authToken;

            if (!string.Equals(authHeader.Trim(), expectedAuth, StringComparison.Ordinal))
            {
                Console.WriteLine("[認証失敗] トークン不一致または欠落 (IP: " + remoteIp + ", Path: " + path + ")");
                res.StatusCode = 401;
                SendJson(res, new Dictionary<string, object>
                {
                    { "status", "error" },
                    { "message", "401 Unauthorized: Invalid or missing Bearer token" }
                });
                return false;
            }
        }

        return true;
    }

    // フェーズ 6-3 (案2 / レビュー 23-1): Bearer 免除の対象は、メソッドとパスで厳密に一致する
    // 次のエンドポイントだけに限る。一覧 (GET)、詳細 (GET {jobId})、写真 (GET {jobId}/photos/{md5})、
    // 実行系 (POST {jobId}/results・errors・complete)。作成・承認・移動・確認・取消は免除しない。
    private static bool IsTrustedClaudeExemptPath(string method, string path)
    {
        if (path == "/api/claude/jobs") return method == "GET";
        string prefix = "/api/claude/jobs/";
        if (!path.StartsWith(prefix)) return false;
        string rest = path.Substring(prefix.Length);
        string[] parts = rest.Split('/');
        if (method == "GET")
        {
            if (parts.Length == 1 && parts[0].Length > 0) return true;
            if (parts.Length == 3 && parts[0].Length > 0 && parts[1] == "photos" && parts[2].Length > 0) return true;
            // フェーズ 6-13 (§47.10-5): ワーカーの参考画像取得も免除対象に明示追加する
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1] == "reference") return true;
            return false;
        }
        if (method == "POST")
        {
            if (parts.Length == 2 && parts[0].Length > 0 && (parts[1] == "results" || parts[1] == "errors" || parts[1] == "complete")) return true;
            return false;
        }
        return false;
    }

    // 送信元が許可アドレスと一致するか。判定は RemoteEndPoint のみで行い、転送ヘッダは使わない。
    // IPv4-mapped IPv6 は IPv4 へ正規化してから比較する。
    internal static bool IsTrustedClaudeIp(IPAddress remote, bool hasForwardedHeaders, string configuredTrustedIp)
    {
        if (hasForwardedHeaders || remote == null || string.IsNullOrEmpty(configuredTrustedIp)) return false;
        IPAddress allowed;
        if (!IPAddress.TryParse(configuredTrustedIp, out allowed)) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (allowed.IsIPv4MappedToIPv6) allowed = allowed.MapToIPv4();
        return remote.Equals(allowed);
    }

    private static bool IsTrustedClaudeIp(HttpListenerRequest req)
    {
        if (req == null) return false;
        if (HasForwardedHeaders(req)) return false;
        IPAddress remote = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address : null;
        return IsTrustedClaudeIp(remote, false, SyncStore.GetTrustedMuseIp());
    }

    private static bool IsTrustedClaudeExemptRequest(HttpListenerRequest req, string path)
    {
        // レビュー 24-1 補足ガード: 転送ヘッダを持つリクエストは免除対象にしない (防御的措置)
        if (req == null || HasForwardedHeaders(req)) return false;
        if (!IsTrustedClaudeExemptPath(req.HttpMethod, path)) return false;
        return IsTrustedClaudeIp(req);
    }

    // 許可アドレスの設定値の検証と正規化。空欄は「免除オフ」として有効。
    // 単一の IP のみ受理し、ループバックと未指定アドレス (0.0.0.0 / ::) は危険なため拒否する。
    private static bool TryNormalizeTrustedIp(string raw, out string normalized)
    {
        normalized = "";
        if (raw == null) raw = "";
        raw = raw.Trim();
        if (raw.Length == 0) return true;
        IPAddress ip;
        if (raw.IndexOf('.') >= 0 && raw.IndexOf(':') < 0)
        {
            // レビュー 24-5: IPv4 は 4 オクテットの正規形のみ受理する。
            // IPAddress.TryParse は "100" や "192.168.1" のような省略表記も受理してしまうため、先に分解して検証する。
            string[] parts = raw.Split('.');
            if (parts.Length != 4) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part.Length > 3) return false;
                if (part.Length > 1 && part[0] == '0') return false;
                int value = 0;
                for (int k = 0; k < part.Length; k++)
                {
                    char c = part[k];
                    if (c < '0' || c > '9') return false;
                    value = value * 10 + (c - '0');
                }
                if (value > 255) return false;
            }
            if (!IPAddress.TryParse(raw, out ip)) return false;
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        }
        else if (raw.IndexOf(':') >= 0)
        {
            if (!IPAddress.TryParse(raw, out ip)) return false;
            if (ip.IsIPv4MappedToIPv6)
            {
                // IPv4-mapped IPv6 は IPv4 のドット表記として検証し直す
                return TryNormalizeTrustedIp(ip.MapToIPv4().ToString(), out normalized);
            }
            // IPv6 は正規形 (ToString の往復一致) のみ受理する
            if (!string.Equals(ip.ToString(), raw, StringComparison.OrdinalIgnoreCase)) return false;
        }
        else
        {
            return false;
        }
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.Equals(IPAddress.Broadcast)) return false;
        byte[] addrBytes = ip.GetAddressBytes();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (addrBytes[0] >= 224 && addrBytes[0] <= 239) return false;
        }
        else
        {
            if (addrBytes[0] == 0xFF) return false;
        }
        normalized = ip.ToString();
        return true;
    }

    private static void HandleUpdateTrustedMuseIp(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string jsonStr = "";
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                jsonStr = reader.ReadToEnd();
            }
            string raw = "";
            if (!string.IsNullOrEmpty(jsonStr))
            {
                JObject json = null;
                try
                {
                    json = JObject.Parse(jsonStr);
                }
                catch (Exception)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "リクエスト形式が不正です" } });
                    return;
                }
                JToken ipToken = json["trustedMuseIp"];
                if (ipToken != null && ipToken.Type != JTokenType.String && ipToken.Type != JTokenType.Null)
                {
                    res.StatusCode = 400;
                    SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "許可アドレスは文字列で指定してください" } });
                    return;
                }
                raw = (ipToken != null && ipToken.Type == JTokenType.String) ? (string)ipToken : "";
            }
            string normalized;
            if (!TryNormalizeTrustedIp(raw, out normalized))
            {
                res.StatusCode = 400;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "許可アドレスの形式が不正です（単一のIPアドレスを指定してください）" } });
                return;
            }
            if (!SyncStore.SetTrustedMuseIp(normalized))
            {
                res.StatusCode = 500;
                SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", "許可アドレスの保存に失敗しました" } });
                return;
            }
            SendJson(res, new Dictionary<string, object> { { "status", "ok" }, { "trustedMuseIp", normalized } });
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            SendJson(res, new Dictionary<string, object> { { "status", "error" }, { "message", ex.Message } });
        }
    }
}
