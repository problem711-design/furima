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
}

public static class ClaudeJobStore
{
    private static readonly object _jobLock = new object();
    private static List<ClaudeJob> _jobList = null;

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
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return _jobList;

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            var jobs = JsonConvert.DeserializeObject<List<ClaudeJob>>(json);
            if (jobs != null)
            {
                foreach (var job in jobs)
                {
                    if (job == null || string.IsNullOrEmpty(job.jobId)) continue;
                    if (job.photos == null) job.photos = new List<ClaudeJobPhoto>();
                    if (job.results == null) job.results = new List<ClaudeJobResult>();
                    if (job.errors == null) job.errors = new List<ClaudeJobError>();
                    _jobList.Add(job);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] claude_jobs.json の読み込みに失敗しました: " + ex.Message);
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
            File.WriteAllText(path, JsonConvert.SerializeObject(_jobList, Formatting.Indented), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] claude_jobs.json の保存に失敗しました: " + ex.Message);
        }
    }

    // 終端状態かつ保持期間を過ぎたジョブを削除する。
    // done / done_with_errors はユーザーが確認済み (acknowledged) のものだけを対象にする。
    private static void CleanupLocked()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var remaining = new List<ClaudeJob>();
        bool changed = false;
        foreach (var job in _jobList)
        {
            bool terminal =
                (job.status == "cancelled" || job.status == "denied") ||
                ((job.status == "done" || job.status == "done_with_errors") && job.acknowledged);
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

    public static ClaudeJob CreateJob(ClaudeJob job)
    {
        if (job == null) return null;
        lock (_jobLock)
        {
            var list = LoadJobsLocked();
            CleanupLocked();
            job.jobId = Guid.NewGuid().ToString();
            job.createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            job.status = "queued";
            job.approvalRequestedAt = 0;
            job.jobToken = null;
            job.jobTokenExpiresAt = 0;
            if (job.photos == null) job.photos = new List<ClaudeJobPhoto>();
            if (job.results == null) job.results = new List<ClaudeJobResult>();
            if (job.errors == null) job.errors = new List<ClaudeJobError>();
            job.completedAt = 0;
            job.acknowledged = false;
            job.acknowledgedAt = 0;
            list.Add(job);
            SaveJobsLocked();
            return job;
        }
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
                copy.Add(job);
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
                    return list[i];
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
                    return list[i];
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
