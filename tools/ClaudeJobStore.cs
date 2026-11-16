using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;

// 「Museに送る」一括画像加工ジョブのモデルと永続化ストア。
// 写真ゴミ箱 (SyncStore の PhotoTrashEntry 管理) と同じ流儀で、
// syncDir/claude_jobs.json へ JSON 保存する。商品データ (items_data.json) とは分離する。
// 内部識別子は claude 、ユーザー向け文言は Muse で統一する (計画書の命名規約)。

public class ClaudeJobPhoto
{
    public string md5 { get; set; }
    public string fileName { get; set; }
}

public class ClaudeJobResult
{
    public string sourceMd5 { get; set; }
    public string resultFileName { get; set; }
    public string resultMd5 { get; set; }
}

public class ClaudeJobError
{
    public string sourceMd5 { get; set; }
    public string message { get; set; }
}

public class ClaudeJob
{
    public string jobId { get; set; }
    public int itemId { get; set; }
    public string itemUuid { get; set; }
    public string itemTitle { get; set; }
    public string folderName { get; set; }
    public List<ClaudeJobPhoto> photos { get; set; }
    public string templateId { get; set; }
    public string templateName { get; set; }
    public string promptText { get; set; }
    public string origin { get; set; }
    public string queue { get; set; }
    public string status { get; set; }
    public long createdAt { get; set; }
    public long approvalRequestedAt { get; set; }
    public string jobToken { get; set; }
    public long jobTokenExpiresAt { get; set; }
    public List<ClaudeJobResult> results { get; set; }
    public List<ClaudeJobError> errors { get; set; }
    public long completedAt { get; set; }
    public bool acknowledged { get; set; }
    public long acknowledgedAt { get; set; }
    public bool errorDismissed { get; set; }
}

// claude_jobs.json の保存形式 (フェーズ 6-1a でルート配列からオブジェクトへ移行)。
// claudeRevision をジョブと一緒に永続化し、サーバー再起動後も単調増加を引き継ぐ。
public class ClaudeJobsFile
{
    public long claudeRevision { get; set; }
    public List<ClaudeJob> jobs { get; set; }
    // フェーズ 6-5: 加工済みラベルの既読時刻 (itemUuid -> ms)。カードを開いたら消える表示の共有状態。
    public Dictionary<string, long> labelSeenAtByItemUuid { get; set; }
}

public static class ClaudeJobStore
{
    private static readonly object _jobLock = new object();
    private static List<ClaudeJob> _jobList = null;
    private static long _claudeRevision = 0;
    private static Dictionary<string, long> _labelSeenAt = new Dictionary<string, long>();

    // 終端ジョブの保持期間 (30日)。写真ゴミ箱の保持期間と同水準。
    private const long RetentionMs = 30L * 24L * 60L * 60L * 1000L;

    private static string GetJobsJsonPath()
    {
        if (string.IsNullOrEmpty(SyncStore.SyncDir)) return "";
        return Path.Combine(SyncStore.SyncDir, "claude_jobs.json");
    }

    private static List<ClaudeJob> LoadJobsLocked()
    {
        if (_jobList != null) return _jobList;
        _jobList = new List<ClaudeJob>();
        string path = GetJobsJsonPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            if (_claudeRevision <= 0) _claudeRevision = 1;
            return _jobList;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            string trimmed = json != null ? json.TrimStart() : "";
            bool migrated = false;
            bool changed = false;
            List<ClaudeJob> jobs = null;
            if (trimmed.StartsWith("["))
            {
                // 旧形式 (ルートが配列): jobs として読み込み、リビジョンは移行時点の Unix 秒で
                // 初期化する (見張りが保持しうる旧カウンタより確実に大きくなる)。
                jobs = JsonConvert.DeserializeObject<List<ClaudeJob>>(json);
                _claudeRevision = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                migrated = true;
            }
            else if (trimmed.StartsWith("{"))
            {
                var file = JsonConvert.DeserializeObject<ClaudeJobsFile>(json);
                if (file != null)
                {
                    jobs = file.jobs;
                    _claudeRevision = file.claudeRevision > 0 ? file.claudeRevision : 1;
                    if (file.labelSeenAtByItemUuid != null)
                    {
                        _labelSeenAt = new Dictionary<string, long>(file.labelSeenAtByItemUuid);
                    }
                }
            }
            if (_claudeRevision <= 0) _claudeRevision = 1;
            if (jobs != null)
            {
                foreach (var job in jobs)
                {
                    if (job == null || string.IsNullOrEmpty(job.jobId)) continue;
                    if (job.photos == null) job.photos = new List<ClaudeJobPhoto>();
                    if (job.results == null) job.results = new List<ClaudeJobResult>();
                    if (job.errors == null) job.errors = new List<ClaudeJobError>();
                    if (job.queue != "patrol" && job.queue != "manual" && job.queue != "waiting")
                    {
                        job.queue = "manual";
                        changed = true;
                    }
                    if (job.status == "approval_expired")
                    {
                        job.status = "queued";
                        job.queue = "waiting";
                        changed = true;
                    }
                    _jobList.Add(job);
                }
            }
            if (migrated)
            {
                Console.WriteLine("[情報] claude_jobs.json を新形式へ移行しました (claudeRevision 初期値: " + _claudeRevision + ")");
                SaveJobsLocked();
            }
            else if (changed)
            {
                SaveJobsLocked();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] claude_jobs.json の読み込みに失敗しました: " + ex.Message);
            if (_claudeRevision <= 0) _claudeRevision = 1;
        }
        return _jobList;
    }

    private static void SaveJobsLocked()
    {
        string path = GetJobsJsonPath();
        if (string.IsNullOrEmpty(path) || _jobList == null) return;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var file = new ClaudeJobsFile
            {
                claudeRevision = _claudeRevision > 0 ? _claudeRevision : 1,
                jobs = _jobList,
                labelSeenAtByItemUuid = _labelSeenAt
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] claude_jobs.json の保存に失敗しました: " + ex.Message);
        }
    }

    // フェーズ 6-5: 加工済みラベルの既読スナップショットを返す (一覧応答に同梱して全端末で共有する)。
    public static Dictionary<string, long> GetLabelSeenAtSnapshot()
    {
        lock (_jobLock)
        {
            LoadJobsLocked();
            return new Dictionary<string, long>(_labelSeenAt);
        }
    }

    // フェーズ 6-5: 商品カードを開いたときに加工済みラベルを既読化する。
    // UI 状態のため claudeRevision は進めない (見張りを起こさない)。
    public static void MarkLabelSeen(string itemUuid)
    {
        if (string.IsNullOrEmpty(itemUuid)) return;
        lock (_jobLock)
        {
            LoadJobsLocked();
            _labelSeenAt[itemUuid] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SaveJobsLocked();
        }
    }

    // 終端状態かつ保持期間を過ぎたジョブを削除する。
    // done はユーザーが確認済み (acknowledged) のものだけを対象にする。
    // done_with_errors は acknowledged に加えて errorDismissed も必須とする
    // (スマホで完了通知を閉じただけで、PC で未対応のエラー履歴が消えないようにするため)。
    private static void CleanupLocked()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var remaining = new List<ClaudeJob>();
        bool changed = false;
        foreach (var job in _jobList)
        {
            bool terminal =
                (job.status == "cancelled" || job.status == "denied") ||
                (job.status == "done" && job.acknowledged) ||
                (job.status == "done_with_errors" && job.acknowledged && job.errorDismissed);
            long refTime = job.completedAt > 0 ? job.completedAt : job.createdAt;
            if (terminal && refTime > 0 && (now - refTime) > RetentionMs)
            {
                changed = true;
                continue;
            }
            remaining.Add(job);
        }
        if (changed)
        {
            _jobList = remaining;
            SaveJobsLocked();
        }
    }

    // 永続化された claudeRevision を返す (サーバー再起動後も単調増加を引き継ぐ)。
    public static long GetRevision()
    {
        lock (_jobLock)
        {
            LoadJobsLocked();
            return _claudeRevision > 0 ? _claudeRevision : 1;
        }
    }

    // claudeRevision を加算して保存し、新しい値を返す。
    public static long IncrementRevision()
    {
        lock (_jobLock)
        {
            LoadJobsLocked();
            if (_claudeRevision <= 0) _claudeRevision = 1;
            _claudeRevision++;
            SaveJobsLocked();
            return _claudeRevision;
        }
    }

    public static ClaudeJob CreateJob(ClaudeJob job)
    {
        if (job == null) return null;
        lock (_jobLock)
        {
            var list = LoadJobsLocked();
            CleanupLocked();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            job.jobId = Guid.NewGuid().ToString();
            job.createdAt = now;
            if (job.queue != "patrol" && job.queue != "manual" && job.queue != "waiting")
            {
                job.queue = "manual";
            }
            // 送信時承認リクエスト (フェーズ 6-1a): 巡回・手動は作成と同時に承認待ちにする。
            // 単一ロック内で初期状態とリビジョン加算までを確定させ、保存は1回だけにする
            // (中間状態の露出と複数回保存を避けるため)。
            if (job.queue == "waiting")
            {
                job.status = "queued";
                job.approvalRequestedAt = 0;
            }
            else
            {
                job.status = "approval_pending";
                job.approvalRequestedAt = now;
            }
            job.jobToken = null;
            job.jobTokenExpiresAt = 0;
            if (job.photos == null) job.photos = new List<ClaudeJobPhoto>();
            if (job.results == null) job.results = new List<ClaudeJobResult>();
            if (job.errors == null) job.errors = new List<ClaudeJobError>();
            job.completedAt = 0;
            job.acknowledged = false;
            job.acknowledgedAt = 0;
            job.errorDismissed = false;
            list.Add(job);
            if (_claudeRevision <= 0) _claudeRevision = 1;
            _claudeRevision++;
            SaveJobsLocked();
            return CloneJob(job);
        }
    }

    // Build 13 (13-2): 呼び出し側へは必ずディープコピーを返す。
    // 生参照を返すと、別スレッドの MutateJob と列挙が競合して例外になり得るため、
    // ジョブ本体だけでなく photos / results / errors の要素まで複製して参照共有を断つ。
    private static ClaudeJob CloneJob(ClaudeJob job)
    {
        if (job == null) return null;
        var clone = new ClaudeJob
        {
            jobId = job.jobId,
            itemId = job.itemId,
            itemUuid = job.itemUuid,
            itemTitle = job.itemTitle,
            folderName = job.folderName,
            photos = new List<ClaudeJobPhoto>(),
            templateId = job.templateId,
            templateName = job.templateName,
            promptText = job.promptText,
            origin = job.origin,
            queue = job.queue,
            status = job.status,
            createdAt = job.createdAt,
            approvalRequestedAt = job.approvalRequestedAt,
            jobToken = job.jobToken,
            jobTokenExpiresAt = job.jobTokenExpiresAt,
            results = new List<ClaudeJobResult>(),
            errors = new List<ClaudeJobError>(),
            completedAt = job.completedAt,
            acknowledged = job.acknowledged,
            acknowledgedAt = job.acknowledgedAt,
            errorDismissed = job.errorDismissed
        };
        if (job.photos != null)
        {
            foreach (var p in job.photos)
            {
                clone.photos.Add(new ClaudeJobPhoto { md5 = p.md5, fileName = p.fileName });
            }
        }
        if (job.results != null)
        {
            foreach (var r in job.results)
            {
                clone.results.Add(new ClaudeJobResult { sourceMd5 = r.sourceMd5, resultFileName = r.resultFileName, resultMd5 = r.resultMd5 });
            }
        }
        if (job.errors != null)
        {
            foreach (var e in job.errors)
            {
                clone.errors.Add(new ClaudeJobError { sourceMd5 = e.sourceMd5, message = e.message });
            }
        }
        return clone;
    }

    public static List<ClaudeJob> GetJobs(string statusFilter)
    {
        lock (_jobLock)
        {
            var list = LoadJobsLocked();
            var copy = new List<ClaudeJob>();
            foreach (var job in list)
            {
                if (!string.IsNullOrEmpty(statusFilter) && job.status != statusFilter) continue;
                copy.Add(CloneJob(job));
            }
            copy.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
            return copy;
        }
    }

    public static ClaudeJob GetJob(string jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return null;
        lock (_jobLock)
        {
            var list = LoadJobsLocked();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].jobId, jobId, StringComparison.OrdinalIgnoreCase))
                {
                    return CloneJob(list[i]);
                }
            }
            return null;
        }
    }

    // ジョブをロック内で変更し、変更があった場合のみ保存する。
    // mutate が true を返したら保存する。対象が無ければ null を返す。
    public static ClaudeJob MutateJob(string jobId, Func<ClaudeJob, bool> mutate)
    {
        if (string.IsNullOrEmpty(jobId) || mutate == null) return null;
        lock (_jobLock)
        {
            var list = LoadJobsLocked();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].jobId, jobId, StringComparison.OrdinalIgnoreCase))
                {
                    if (mutate(list[i]))
                    {
                        SaveJobsLocked();
                    }
                    return CloneJob(list[i]);
                }
            }
            return null;
        }
    }

    // 実行前 (queued / approval_pending / approved) のジョブだけを取り消せる。
    // 戻り値: 0=対象なし, 1=取消成功, 2=取消不可の状態
    public static int CancelJob(string jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return 0;
        lock (_jobLock)
        {
            var list = LoadJobsLocked();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].jobId, jobId, StringComparison.OrdinalIgnoreCase))
                {
                    var job = list[i];
                    if (job.status == "queued" || job.status == "approval_pending" || job.status == "approved")
                    {
                        job.status = "cancelled";
                        job.completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        job.jobToken = null;
                        job.jobTokenExpiresAt = 0;
                        SaveJobsLocked();
                        return 1;
                    }
                    return 2;
                }
            }
            return 0;
        }
    }

    // サーバー起動時に呼ぶ。古い終端ジョブの掃除だけを行う。
    public static void CleanupOnStartup()
    {
        lock (_jobLock)
        {
            LoadJobsLocked();
            CleanupLocked();
        }
    }
}
