package com.example.myapplication.data.server

// フェーズ 6-10 (§37.1 / §41.5): 写真単位の Muse ラベル導出の純粋関数版。
// PC 管理画面 (DashboardHtml.cs の claudePhotoLabel) と同一の述語を保ち、
// 共通ゴールデン仕様表 (§41.5) の単体テストから直接検証できるよう Android 依存を持たない。
// 状態の意味: processing=加工中 / approved=開始待ち (未着手) / approval_pending=承認待ち /
// queued=待機中 / done・done_with_errors の結果到着=加工済み (3 日窓・既読で消滅)。
object ClaudeLabelDerivation {

    const val DONE_WINDOW_MS: Long = 3L * 24 * 60 * 60 * 1000

    fun photoLabelKey(
        bundleUuids: Collection<String>,
        md5: String,
        jobs: List<ClaudeJobDto>,
        seenAtMap: Map<String, Long>,
        snapshotMap: Map<String, Long>,
        now: Long
    ): String? {
        if (md5.isBlank()) return null
        val target = md5.lowercase()
        val bundleSet = bundleUuids.map { it.lowercase() }.toSet()
        if (bundleSet.isEmpty()) return null
        val itemJobs = jobs.filter { it.itemUuid.lowercase() in bundleSet }
        // エラー: supersede されていないものが1件でもあればエラー (34-3 の厳密条件)
        for (errJob in itemJobs) {
            if (errJob.status != "done_with_errors" || errJob.errorDismissed) continue
            if (errJob.errors.none { it.sourceMd5.lowercase() == target }) continue
            val errTime = if (errJob.completedAt > 0) errJob.completedAt else errJob.createdAt
            val superseded = itemJobs.any { ok ->
                (ok.status == "done" || ok.status == "done_with_errors") && ok.completedAt > errTime &&
                    ok.results.any { it.sourceMd5.lowercase() == target }
            }
            if (!superseded) return "error"
        }
        var hasProcessing = false
        var hasApproval = false
        var hasStartWait = false
        var hasWaiting = false
        var hasDone = false
        for (job in itemJobs) {
            if (job.photos.any { it.md5.lowercase() == target }) {
                when (job.status) {
                    "processing" -> hasProcessing = true
                    "approval_pending" -> hasApproval = true
                    // フェーズ 6-10 (§37.1): approved は未着手なので開始待ちとして分離する
                    "approved" -> hasStartWait = true
                    "queued" -> hasWaiting = true
                }
            }
            // 加工済みは生成物 (resultMd5) 基準で、所属チェックの外で判定する (32-1)
            // 既読時刻はジョブの所属商品ごとに引く (49-1: スナップショット優先は PC と同型)
            if ((job.status == "done" || job.status == "done_with_errors") && job.results.any { it.resultMd5.lowercase() == target }) {
                val jobSeenAt = snapshotMap[job.itemUuid.lowercase()] ?: (seenAtMap[job.itemUuid.lowercase()] ?: 0L)
                if (job.completedAt > jobSeenAt && (now - job.completedAt) <= DONE_WINDOW_MS) hasDone = true
            }
        }
        return when {
            hasProcessing -> "processing"
            hasApproval -> "approval"
            hasStartWait -> "startwait"
            hasWaiting -> "waiting"
            hasDone -> "done"
            else -> null
        }
    }

    // フェーズ 6-10 (§37.4 / §40.3): ラベルの源泉ジョブ (チップタップ取消の対象)。
    // その写真を含むアクティブ状態のジョブのうち、ラベル優先順位が最も高いもの
    // (同順位では最古) を返す。ただし本関数は未解決エラーや加工済みの有無を見ないため、
    // エラー表示中の写真でも裏にアクティブなジョブがあればそれを返し得る。
    // 呼び出し側は、表示中のラベルがアクティブ 4 状態 (processing / approval / startwait /
    // waiting) のときに限って取消に使うこと (レビュー 64-1)。
    fun labelSourceJob(
        bundleUuids: Collection<String>,
        md5: String,
        jobs: List<ClaudeJobDto>
    ): ClaudeJobDto? {
        if (md5.isBlank()) return null
        val target = md5.lowercase()
        val bundleSet = bundleUuids.map { it.lowercase() }.toSet()
        if (bundleSet.isEmpty()) return null
        val priority = mapOf("processing" to 0, "approval_pending" to 1, "approved" to 2, "queued" to 3)
        return jobs
            .filter { job ->
                job.itemUuid.lowercase() in bundleSet &&
                    priority.containsKey(job.status) &&
                    job.photos.any { it.md5.lowercase() == target }
            }
            .minWithOrNull(compareBy<ClaudeJobDto> { priority[it.status] ?: 99 }.thenBy { it.createdAt })
    }
}
