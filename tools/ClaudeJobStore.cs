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
    // フェーズ 6-13 (§47.10): 参考画像の添付 (任意・1 枚)。未指定は "" で統一する。
    public string referenceMd5 { get; set; }
    public string referenceFileName { get; set; }
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
    // フェーズ 6-5 是正 (32-3): 既読専用の単調増加カウンタ。見張り用 claudeRevision とは分離する。
    public long labelSeenRevision { get; set; }
}

public static class ClaudeJobStore
{
    private static readonly object _jobLock = new object();
    private static List<ClaudeJob> _jobList = null;
    private static long _claudeRevision = 0;
    private static Dictionary<string, long> _labelSeenAt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private static long _labelSeenRevision = 0;

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
                    _labelSeenRevision = file.labelSeenRevision > 0 ? file.labelSeenRevision : 1;
                    if (file.labelSeenAtByItemUuid != null)
                    {
                        // キーは小文字へ統一し、大小文字を区別しない辞書で保持する (32-6)
                        _labelSeenAt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in file.labelSeenAtByItemUuid)
                        {
                            if (!string.IsNullOrEmpty(kv.Key)) _labelSeenAt[kv.Key.Trim().ToLowerInvariant()] = kv.Value;
                        }
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
                    if (job.referenceMd5 == null) job.referenceMd5 = "";
                    if (job.referenceFileName == null) job.referenceFileName = "";
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
                labelSeenAtByItemUuid = _labelSeenAt,
                labelSeenRevision = _labelSeenRevision > 0 ? _labelSeenRevision : 1
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
    // フェーズ 6-5 是正 (34-1): 複数 UUID の一括既読化。単一ロック・単一保存で、
    // 全対象に同一のサーバー確定時刻を設定し、実際に変化があった場合のみ
    // labelSeenRevision を進める。戻り値はサーバー確定の既読時刻 (対象なしは 0)。
    public static long MarkLabelsSeen(List<string> itemUuids)
    {
        if (itemUuids == null || itemUuids.Count == 0) return 0;
        lock (_jobLock)
        {
            LoadJobsLocked();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool changed = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in itemUuids)
            {
                string uuid = (raw ?? "").Trim().ToLowerInvariant();
                if (uuid.Length == 0 || !seen.Add(uuid)) continue;
                long current;
                if (!_labelSeenAt.TryGetValue(uuid, out current) || current != now)
                {
                    _labelSeenAt[uuid] = now;
                    changed = true;
                }
            }
            if (!changed) return now;
            if (_labelSeenRevision <= 0) _labelSeenRevision = 1;
            _labelSeenRevision++;
            SaveJobsLocked();
            return now;
        }
    }

    public static long GetLabelSeenRevision()
    {
        lock (_jobLock)
        {
            LoadJobsLocked();
            return _labelSeenRevision > 0 ? _labelSeenRevision : 1;
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
        // フェーズ 6-5 是正 (32-7-3): 関連ジョブが無く既読から保持期間を超えた既読エントリも掃除する。
        // この削除では labelSeenRevision は進めず、保存だけを行う (34-4-3)。
        bool seenPruned = false;
        var seenKeys = new List<string>(_labelSeenAt.Keys);
        foreach (var key in seenKeys)
        {
            long seenAt = _labelSeenAt[key];
            if ((now - seenAt) <= RetentionMs) continue;
            bool hasJob = false;
            foreach (var job in remaining)
            {
                if (job != null && string.Equals(job.itemUuid, key, StringComparison.OrdinalIgnoreCase)) { hasJob = true; break; }
            }
            if (!hasJob)
            {
                _labelSeenAt.Remove(key);
                seenPruned = true;
            }
        }
        if (changed || seenPruned)
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
            if (job.referenceMd5 == null) job.referenceMd5 = "";
            if (job.referenceFileName == null) job.referenceFileName = "";
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
            referenceMd5 = job.referenceMd5 ?? "",
            referenceFileName = job.referenceFileName ?? "",
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

    // フェーズ 6-5 是正 (34-3 / §36.2-2): 完了確定と同一ロック内でまとめて保存する変更。
    // mutate 適用後、同一商品の旧エラーのうち全て supersede 済みとなったものを自動解消する。
    public static ClaudeJob MutateJobWithErrorSupersede(string jobId, Func<ClaudeJob, bool> mutate)
    {
        return MutateJobWithErrorSupersede(jobId, mutate, null);
    }

    // フェーズ 6-6 是正 (Build 10 / §41.2・§42): bundleUuids が与えられた場合、自動解消の判定範囲を
    // まとめ UUID 集合全体へ広げ、PC 管理画面のラベル判定 (まとめ集合) と一致させる。
    // bundleUuids が null の場合は従来どおり対象ジョブ自身の商品のみを範囲とする。
    public static ClaudeJob MutateJobWithErrorSupersede(string jobId, Func<ClaudeJob, bool> mutate, System.Collections.Generic.ICollection<string> bundleUuids)
    {
        if (string.IsNullOrEmpty(jobId) || mutate == null) return null;
        lock (_jobLock)
        {
            var list = LoadJobsLocked();
            ClaudeJob target = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].jobId, jobId, StringComparison.OrdinalIgnoreCase)) { target = list[i]; break; }
            }
            if (target == null) return null;
            if (mutate(target))
            {
                AutoDismissSupersededErrorsLocked(bundleUuids, target.itemUuid);
                SaveJobsLocked();
            }
            return CloneJob(target);
        }
    }

    // 判定範囲 (uuidSet) 内の全ての成功ジョブを対象に、旧ジョブの全エラーが supersede 済みか判定する。
    // supersede 条件 (計画 §27.1.1): okJob.completedAt > errTime かつ results に小文字一致の sourceMd5。
    // 分割再加工 (前ジョブで M1 成功・今回ジョブで M2 成功) でも全エラー解消になれば解消済みにする。
    private static void AutoDismissSupersededErrorsLocked(System.Collections.Generic.ICollection<string> bundleUuids, string fallbackItemUuid)
    {
        var uuidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (bundleUuids != null)
        {
            foreach (var u in bundleUuids) { if (!string.IsNullOrEmpty(u)) uuidSet.Add(u); }
        }
        if (!string.IsNullOrEmpty(fallbackItemUuid)) uuidSet.Add(fallbackItemUuid);
        if (uuidSet.Count == 0) return;
        var okJobs = new List<ClaudeJob>();
        foreach (var j in _jobList)
        {
            if (j != null && j.itemUuid != null && uuidSet.Contains(j.itemUuid)
                && (j.status == "done" || j.status == "done_with_errors") && j.completedAt > 0)
            {
                okJobs.Add(j);
            }
        }
        if (okJobs.Count == 0) return;
        foreach (var errJob in _jobList)
        {
            if (errJob == null || errJob.status != "done_with_errors" || errJob.errorDismissed) continue;
            if (errJob.itemUuid == null || !uuidSet.Contains(errJob.itemUuid)) continue;
            if (errJob.errors == null || errJob.errors.Count == 0) continue;
            long errTime = errJob.completedAt > 0 ? errJob.completedAt : errJob.createdAt;
            bool allSuperseded = true;
            foreach (var err in errJob.errors)
            {
                string md5 = (err.sourceMd5 ?? "").Trim().ToLowerInvariant();
                bool superseded = false;
                foreach (var ok in okJobs)
                {
                    if (ok.completedAt <= errTime || ok.results == null) continue;
                    foreach (var r in ok.results)
                    {
                        if (string.Equals((r.sourceMd5 ?? "").Trim(), md5, StringComparison.OrdinalIgnoreCase)) { superseded = true; break; }
                    }
                    if (superseded) break;
                }
                if (!superseded) { allSuperseded = false; break; }
            }
            if (allSuperseded) errJob.errorDismissed = true;
        }
    }

    // 実行前 (queued / approval_pending / approved) に加え、フェーズ 6-10 (§37.4) で
    // processing も取り消せるようにした (13-3 の方針の意図的な変更)。
    // 取消前に返却済みの結果は残り、未処理の写真はもう処理されない。
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
                    if (job.status == "queued" || job.status == "approval_pending" || job.status == "approved" || job.status == "processing")
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

// フェーズ 6-13 (§47.10): 参考画像コレクションの登録エントリ。
// 実ファイルは商品写真ストアとは完全分離し、専用ディレクトリ _reference_photos に
// {md5}.{ext} で置く。索引は syncDir/claude_references.json に保存する。
public class ClaudeReferenceEntry
{
    public string md5 { get; set; }
    public string displayName { get; set; }
    public string fileName { get; set; }
    public long addedAt { get; set; }
}

public class ClaudeReferencesFile
{
    public List<ClaudeReferenceEntry> references { get; set; }
}

public static class ReferencePhotoStore
{
    private static readonly object _refLock = new object();
    private static List<ClaudeReferenceEntry> _refList = null;

    public static string GetPhotosDir()
    {
        if (string.IsNullOrEmpty(SyncStore.SyncDir)) return "";
        return Path.Combine(SyncStore.SyncDir, "_reference_photos");
    }

    public static string GetThumbsDir()
    {
        if (string.IsNullOrEmpty(SyncStore.SyncDir)) return "";
        return Path.Combine(SyncStore.SyncDir, "_reference_thumbs");
    }

    public static string GetInboxDir()
    {
        if (string.IsNullOrEmpty(SyncStore.SyncDir)) return "";
        return Path.Combine(SyncStore.SyncDir, "_reference_inbox");
    }

    private static string GetJsonPath()
    {
        if (string.IsNullOrEmpty(SyncStore.SyncDir)) return "";
        return Path.Combine(SyncStore.SyncDir, "claude_references.json");
    }

    public static void EnsureDirs()
    {
        try
        {
            string photos = GetPhotosDir();
            if (!string.IsNullOrEmpty(photos) && !Directory.Exists(photos)) Directory.CreateDirectory(photos);
            string thumbs = GetThumbsDir();
            if (!string.IsNullOrEmpty(thumbs) && !Directory.Exists(thumbs)) Directory.CreateDirectory(thumbs);
            string inbox = GetInboxDir();
            if (!string.IsNullOrEmpty(inbox) && !Directory.Exists(inbox)) Directory.CreateDirectory(inbox);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] 参考画像ディレクトリの作成に失敗しました: " + ex.Message);
        }
    }

    // §47.11-3: {md5} を受ける全ルートで使う形式検証 (32 桁の小文字 16 進数)。
    public static bool IsValidMd5(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!ok) return false;
        }
        return true;
    }

    private static List<ClaudeReferenceEntry> LoadLocked()
    {
        if (_refList != null) return _refList;
        _refList = new List<ClaudeReferenceEntry>();
        string path = GetJsonPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return _refList;
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            var file = JsonConvert.DeserializeObject<ClaudeReferencesFile>(json);
            if (file != null && file.references != null)
            {
                foreach (var e in file.references)
                {
                    if (e == null || !IsValidMd5((e.md5 ?? "").Trim().ToLowerInvariant())) continue;
                    e.md5 = e.md5.Trim().ToLowerInvariant();
                    if (e.displayName == null) e.displayName = "";
                    if (e.fileName == null) e.fileName = "";
                    _refList.Add(e);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] claude_references.json の読み込みに失敗しました: " + ex.Message);
        }
        return _refList;
    }

    private static void SaveLocked()
    {
        string path = GetJsonPath();
        if (string.IsNullOrEmpty(path) || _refList == null) return;
        try
        {
            var file = new ClaudeReferencesFile { references = _refList };
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] claude_references.json の保存に失敗しました: " + ex.Message);
        }
    }

    private static ClaudeReferenceEntry CloneEntry(ClaudeReferenceEntry e)
    {
        if (e == null) return null;
        return new ClaudeReferenceEntry
        {
            md5 = e.md5 ?? "",
            displayName = e.displayName ?? "",
            fileName = e.fileName ?? "",
            addedAt = e.addedAt
        };
    }

    public static List<ClaudeReferenceEntry> GetEntries()
    {
        lock (_refLock)
        {
            var list = LoadLocked();
            var copy = new List<ClaudeReferenceEntry>();
            foreach (var e in list) copy.Add(CloneEntry(e));
            copy.Sort((a, b) => a.addedAt.CompareTo(b.addedAt));
            return copy;
        }
    }

    public static ClaudeReferenceEntry GetEntry(string md5)
    {
        if (!IsValidMd5(md5)) return null;
        lock (_refLock)
        {
            var list = LoadLocked();
            foreach (var e in list)
            {
                if (string.Equals(e.md5, md5, StringComparison.Ordinal)) return CloneEntry(e);
            }
            return null;
        }
    }

    public static bool IsRegistered(string md5)
    {
        return GetEntry(md5) != null;
    }

    // 既に登録済みの場合は false を返し、既存エントリ (表示名等) を維持する。
    public static bool TryRegister(string md5, string displayName, string fileName)
    {
        if (!IsValidMd5(md5)) return false;
        lock (_refLock)
        {
            var list = LoadLocked();
            foreach (var e in list)
            {
                if (string.Equals(e.md5, md5, StringComparison.Ordinal)) return false;
            }
            list.Add(new ClaudeReferenceEntry
            {
                md5 = md5,
                displayName = displayName ?? "",
                fileName = fileName ?? "",
                addedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            SaveLocked();
            return true;
        }
    }

    public static bool Unregister(string md5)
    {
        if (!IsValidMd5(md5)) return false;
        lock (_refLock)
        {
            var list = LoadLocked();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].md5, md5, StringComparison.Ordinal))
                {
                    list.RemoveAt(i);
                    SaveLocked();
                    return true;
                }
            }
            return false;
        }
    }

    // 実ファイルのパスを解決する。登録エントリの fileName を優先し、
    // 登録解除済みだが未完了ジョブ参照のため保持されている実体は {md5}.* の探索で見つける。
    public static string ResolveFilePath(string md5)
    {
        if (!IsValidMd5(md5)) return null;
        string dir = GetPhotosDir();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        var entry = GetEntry(md5);
        if (entry != null && !string.IsNullOrEmpty(entry.fileName))
        {
            string named = Path.Combine(dir, entry.fileName);
            if (File.Exists(named)) return named;
        }
        try
        {
            string[] found = Directory.GetFiles(dir, md5 + ".*");
            if (found != null && found.Length > 0 && File.Exists(found[0])) return found[0];
        }
        catch (Exception)
        {
        }
        return null;
    }

    public static void DeleteFileAndThumb(string md5)
    {
        if (!IsValidMd5(md5)) return;
        try
        {
            string path = ResolveFilePath(md5);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            string thumbs = GetThumbsDir();
            if (!string.IsNullOrEmpty(thumbs))
            {
                string thumb = Path.Combine(thumbs, md5 + ".jpg");
                if (File.Exists(thumb)) File.Delete(thumb);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[警告] 参考画像ファイルの削除に失敗しました: " + ex.Message);
        }
    }

    // 登録されておらず、未完了ジョブからも参照されていない実ファイルを回収する (§47.10-5)。
    // referencedMd5s は呼び出し側が未完了ジョブ (queued / approval_pending / approved / processing)
    // から集めた参考画像 md5 の集合。戻り値は回収した件数。
    public static int SweepOrphans(HashSet<string> referencedMd5s)
    {
        string dir = GetPhotosDir();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
        int removed = 0;
        string[] files;
        try
        {
            files = Directory.GetFiles(dir);
        }
        catch (Exception)
        {
            return 0;
        }
        if (files == null) return 0;
        foreach (string path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? "";
            string md5 = name.Trim().ToLowerInvariant();
            if (!IsValidMd5(md5)) continue;
            if (IsRegistered(md5)) continue;
            if (referencedMd5s != null && referencedMd5s.Contains(md5)) continue;
            DeleteFileAndThumb(md5);
            removed++;
        }
        return removed;
    }
}
