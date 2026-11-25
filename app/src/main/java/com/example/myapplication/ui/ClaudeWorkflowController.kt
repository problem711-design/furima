package com.example.myapplication.ui

import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.server.ClaudeJobDto
import com.example.myapplication.data.server.ClaudeLabelDerivation
import com.example.myapplication.data.server.ClaudeReferenceDto
import com.example.myapplication.data.server.PcServerClient
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.File

internal class ClaudeWorkflowController(
    private val scope: CoroutineScope,
    private val repository: FleaMarketRepository,
    private val pcServerClient: PcServerClient,
    private val pcServerUrl: () -> String,
    private val pcAuthToken: () -> String,
    private val isPcServerConnected: () -> Boolean,
    private val lastHeartbeatResponseAtMs: () -> Long,
    private val onTemplateRegisteredRefreshRevisions: suspend (String, String) -> Unit
) {
    private var claudeWatchJob: Job? = null
    private val claudeRefreshMutex = Mutex()

    @Volatile var lastKnownClaudeRevision: Long = -1L
        private set
    @Volatile var lastKnownLabelSeenRevision: Long = 0L
        private set
    @Volatile private var markSeenSeq: Long = 0L

    private val _pendingClaudeApprovals = MutableStateFlow<List<ClaudeJobDto>>(emptyList())
    val pendingClaudeApprovals: StateFlow<List<ClaudeJobDto>> = _pendingClaudeApprovals.asStateFlow()

    private val _claudeJobsForLabels = MutableStateFlow<List<ClaudeJobDto>>(emptyList())
    val claudeJobsForLabels: StateFlow<List<ClaudeJobDto>> = _claudeJobsForLabels.asStateFlow()

    private val _claudeLabelSeenAt = MutableStateFlow<Map<String, Long>>(emptyMap())
    val claudeLabelSeenAt: StateFlow<Map<String, Long>> = _claudeLabelSeenAt.asStateFlow()

    private val _labelVisitSnapshot = MutableStateFlow<Map<String, Long>>(emptyMap())
    val labelVisitSnapshot: StateFlow<Map<String, Long>> = _labelVisitSnapshot.asStateFlow()

    private val _claudeLabelTick = MutableStateFlow(0L)

    val claudeItemLabelSummaries: StateFlow<Map<Int, FleaMarketViewModel.ClaudeItemLabelSummary>> =
        combine(
            repository.getAllItemsWithPhotos(),
            _claudeJobsForLabels,
            _claudeLabelSeenAt,
            _claudeLabelTick
        ) { allItems, jobs, seenAt, _ ->
            buildClaudeItemLabelSummaries(allItems, jobs, seenAt)
        }.stateIn(scope, SharingStarted.WhileSubscribed(5000), emptyMap())

    private val _labelBundleUuids = MutableStateFlow<List<String>>(emptyList())
    val labelBundleUuids: StateFlow<List<String>> = _labelBundleUuids.asStateFlow()

    private val _dismissedClaudeApprovalIds = MutableStateFlow<Set<String>>(emptySet())
    val dismissedClaudeApprovalIds: StateFlow<Set<String>> = _dismissedClaudeApprovalIds.asStateFlow()

    private val _pendingClaudeApprovalThumbnails = MutableStateFlow<Map<String, String>>(emptyMap())
    val pendingClaudeApprovalThumbnails: StateFlow<Map<String, String>> = _pendingClaudeApprovalThumbnails.asStateFlow()

    private val _claudeApprovalPeekRequested = MutableStateFlow(false)
    val claudeApprovalPeekRequested: StateFlow<Boolean> = _claudeApprovalPeekRequested.asStateFlow()

    private val _unackedClaudeCompletions = MutableStateFlow<List<ClaudeJobDto>>(emptyList())
    val unackedClaudeCompletions: StateFlow<List<ClaudeJobDto>> = _unackedClaudeCompletions.asStateFlow()

    private val _dismissedClaudeCompletionIds = MutableStateFlow<Set<String>>(emptySet())
    val dismissedClaudeCompletionIds: StateFlow<Set<String>> = _dismissedClaudeCompletionIds.asStateFlow()

    private val _claudeActionInProgress = MutableStateFlow(false)
    val claudeActionInProgress: StateFlow<Boolean> = _claudeActionInProgress.asStateFlow()

    private val _claudeActionMessage = MutableStateFlow<String?>(null)
    val claudeActionMessage: StateFlow<String?> = _claudeActionMessage.asStateFlow()

    fun refreshClaudeItemLabels() {
        _claudeLabelTick.value = System.currentTimeMillis()
    }

    fun clearClaudeActionMessage() {
        _claudeActionMessage.value = null
    }

    fun dismissClaudeApprovalForNow() {
        _dismissedClaudeApprovalIds.value = _pendingClaudeApprovals.value.map { it.jobId }.toSet()
        _claudeApprovalPeekRequested.value = false
    }

    fun requestClaudeApprovalPeek() {
        _dismissedClaudeApprovalIds.value = emptySet()
        _claudeApprovalPeekRequested.value = true
    }

    fun dismissClaudeCompletionForNow(jobId: String) {
        _dismissedClaudeCompletionIds.value = _dismissedClaudeCompletionIds.value + jobId
    }

    fun claudeFreeTextHistory(): List<String> = pcServerClient.getClaudeFreeTextHistory()

    fun sendPhotosToClaude(
        itemId: Int,
        itemUuid: String,
        photos: List<Pair<String, String>>,
        templateId: String,
        queue: String = "patrol",
        promptText: String? = null,
        customPromptName: String? = null,
        registerTemplateName: String? = null,
        referenceMd5: String = "",
        stampNumber: Boolean = true,
        referenceFileName: String = "",
        onError: (String) -> Unit = {},
        onSuccess: (PcServerClient.ClaudeJobCreateResult) -> Unit
    ) {
        scope.launch {
            if (!isPcServerConnected()) {
                onError("PCサーバー未接続です")
                return@launch
            }
            val result = pcServerClient.createClaudeJob(
                itemId, itemUuid, photos, templateId, queue,
                promptText = promptText,
                customPromptName = customPromptName,
                registerTemplateName = registerTemplateName,
                referenceMd5 = referenceMd5,
                stampNumber = stampNumber,
                referenceFileName = referenceFileName
            )
            if (result.success) {
                if (!promptText.isNullOrBlank()) {
                    pcServerClient.pushClaudeFreeTextHistory(promptText)
                }
                if (result.templateRegistered) {
                    try {
                        if (pcServerClient.fetchServerSettingsAfterTemplateRegistration()) {
                            onTemplateRegisteredRefreshRevisions(pcServerUrl(), pcAuthToken())
                        }
                    } catch (_: Exception) {
                    }
                }
                onSuccess(result)
            } else {
                onError(result.message)
            }
        }
    }

    suspend fun fetchClaudeReferences(): List<ClaudeReferenceDto>? {
        if (!isPcServerConnected()) return null
        return pcServerClient.fetchClaudeReferences()?.references
    }

    fun claudeReferenceImageUrl(md5: String, thumb: Boolean): Pair<String, String>? {
        return pcServerClient.claudeReferenceImageUrl(md5, thumb)
    }

    private fun buildClaudeItemLabelSummaries(
        allItems: List<ItemWithPhotos>,
        jobs: List<ClaudeJobDto>,
        seenAt: Map<String, Long>
    ): Map<Int, FleaMarketViewModel.ClaudeItemLabelSummary> {
        val priority = listOf("error", "processing", "approval", "startwait", "waiting", "done")
        val childrenByParent = allItems.filter { it.item.mergedParentId != null }
            .groupBy { it.item.mergedParentId!! }
        val result = mutableMapOf<Int, FleaMarketViewModel.ClaudeItemLabelSummary>()
        for (top in allItems) {
            if (top.item.mergedParentId != null) continue
            val bundleItems = mutableListOf(top)
            val visited = mutableSetOf(top.item.id)
            var frontier = listOf(top.item.id)
            while (frontier.isNotEmpty()) {
                val next = mutableListOf<Int>()
                for (pid in frontier) {
                    for (child in childrenByParent[pid].orEmpty()) {
                        if (!visited.add(child.item.id)) continue
                        bundleItems.add(child)
                        next.add(child.item.id)
                    }
                }
                frontier = next
            }
            val bundleUuids = bundleItems.map { it.item.syncUuid }.filter { it.isNotBlank() }
            val counts = mutableMapOf<String, Int>()
            for (photo in top.photos) {
                val md5 = photo.md5Hash
                if (md5.isBlank()) continue
                val key = claudePhotoLabelKey(top.item.syncUuid, bundleUuids, md5, jobs, seenAt, emptyMap()) ?: continue
                counts[key] = (counts[key] ?: 0) + 1
            }
            val best = priority.firstOrNull { counts.containsKey(it) } ?: continue
            result[top.item.id] = FleaMarketViewModel.ClaudeItemLabelSummary(best, counts[best] ?: 0)
        }
        return result
    }

    fun claudePhotoLabelKey(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobs: List<ClaudeJobDto>,
        seenAtMap: Map<String, Long>,
        snapshotMap: Map<String, Long>
    ): String? {
        if (itemUuid.isBlank() || md5.isBlank()) return null
        val bundleSet = bundleUuids.ifEmpty { listOf(itemUuid) }
        return ClaudeLabelDerivation.photoLabelKey(
            bundleSet, md5, jobs, seenAtMap, snapshotMap, System.currentTimeMillis()
        )
    }

    fun claudePhotoLabelSourceJob(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobs: List<ClaudeJobDto>
    ): ClaudeJobDto? {
        if (itemUuid.isBlank() || md5.isBlank()) return null
        val bundleSet = bundleUuids.ifEmpty { listOf(itemUuid) }
        return ClaudeLabelDerivation.labelSourceJob(bundleSet, md5, jobs)
    }

    suspend fun refreshClaudeJobStates(url: String, token: String, fromWatch: Boolean = false): Boolean {
        return claudeRefreshMutex.withLock {
            if (fromWatch && _claudeActionInProgress.value) return@withLock false
            val seqAtStart = markSeenSeq
            val fetched = pcServerClient.fetchClaudeJobs(url, token) ?: return@withLock false
            val jobs = fetched.jobs
            _claudeJobsForLabels.value = jobs
            if (markSeenSeq == seqAtStart) {
                _claudeLabelSeenAt.value = fetched.labelSeenAtByItemUuid.mapKeys { it.key.lowercase() }
                lastKnownLabelSeenRevision = fetched.labelSeenRevision
            }
            val pending = jobs
                .filter { it.status == "approval_pending" }
                .sortedBy { it.createdAt }
            _pendingClaudeApprovals.value = pending
            val pendingIds = pending.map { it.jobId }.toSet()
            _dismissedClaudeApprovalIds.value = _dismissedClaudeApprovalIds.value.intersect(pendingIds)
            if (pending.isEmpty()) {
                _claudeApprovalPeekRequested.value = false
            }
            _pendingClaudeApprovalThumbnails.value = resolveClaudeApprovalThumbnails(pending)
            val unacked = jobs.filter {
                (it.status == "done" || it.status == "done_with_errors") && !it.acknowledged
            }
            _unackedClaudeCompletions.value = unacked
            _dismissedClaudeCompletionIds.value =
                _dismissedClaudeCompletionIds.value.intersect(unacked.map { it.jobId }.toSet())
            if (fetched.claudeRevision > 0L) {
                lastKnownClaudeRevision = fetched.claudeRevision
            }
            true
        }
    }

    fun cancelClaudeJobFromLabel(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobId: String
    ) {
        val url = pcServerUrl()
        val token = pcAuthToken()
        if (url.isBlank() || jobId.isBlank()) return
        if (_claudeActionInProgress.value) return
        _claudeActionInProgress.value = true
        scope.launch(Dispatchers.IO) {
            try {
                val refreshed = try {
                    refreshClaudeJobStates(url, token)
                } catch (_: Exception) {
                    false
                }
                if (!refreshed) {
                    _claudeActionMessage.value = "最新のジョブ状態を取得できなかったため、取り消しませんでした。接続を確認してください。"
                    return@launch
                }
                val source = claudePhotoLabelSourceJob(itemUuid, bundleUuids, md5, _claudeJobsForLabels.value)
                if (source == null || source.jobId != jobId) {
                    _claudeActionMessage.value = "ジョブの状態が変わったため、取り消しませんでした。最新の表示を確認してください。"
                    return@launch
                }
                val ok = pcServerClient.postClaudeJobCancel(jobId, url, token)
                try {
                    refreshClaudeJobStates(url, token)
                } catch (_: Exception) {
                }
                _claudeActionMessage.value = if (ok) "ジョブを取り消しました" else "取り消しを送信できませんでした。接続を確認してもう一度お試しください。"
            } finally {
                _claudeActionInProgress.value = false
            }
        }
    }

    private suspend fun resolveBundleUuids(itemId: Int, itemUuid: String): List<String> {
        val result = mutableListOf(itemUuid)
        val visitedIds = mutableSetOf(itemId)
        var frontier = listOf(itemId)
        while (frontier.isNotEmpty()) {
            val next = mutableListOf<Int>()
            for (pid in frontier) {
                val children = try { repository.getMergedChildItemsWithPhotos(pid) } catch (_: Exception) { emptyList() }
                for (child in children) {
                    if (!visitedIds.add(child.item.id)) continue
                    if (child.item.syncUuid.isNotBlank()) result.add(child.item.syncUuid)
                    next.add(child.item.id)
                }
            }
            frontier = next
        }
        return result
    }

    fun beginLabelVisit(itemId: Int, itemUuid: String) {
        if (itemUuid.isBlank()) return
        val key = itemUuid.lowercase()
        if (!_labelVisitSnapshot.value.containsKey(key)) {
            _labelVisitSnapshot.value = mapOf(key to (_claudeLabelSeenAt.value[key] ?: 0L))
        }
        _labelBundleUuids.value = listOf(itemUuid)
        if (itemId <= 0) {
            markClaudeLabelSeen(listOf(itemUuid))
            return
        }
        scope.launch(Dispatchers.IO) {
            val bundle = resolveBundleUuids(itemId, itemUuid)
            if (!_labelVisitSnapshot.value.containsKey(key)) return@launch
            _labelBundleUuids.value = bundle
            val additions = bundle.map { it.lowercase() }
                .filter { !_labelVisitSnapshot.value.containsKey(it) }
                .associateWith { _claudeLabelSeenAt.value[it] ?: 0L }
            if (additions.isNotEmpty()) {
                _labelVisitSnapshot.value = _labelVisitSnapshot.value + additions
            }
            markClaudeLabelSeen(bundle)
        }
    }

    fun endLabelVisit() {
        if (_labelVisitSnapshot.value.isNotEmpty()) _labelVisitSnapshot.value = emptyMap()
        if (_labelBundleUuids.value.isNotEmpty()) _labelBundleUuids.value = emptyList()
    }

    fun markClaudeLabelSeen(itemUuids: List<String>) {
        val keys = itemUuids.filter { it.isNotBlank() }.map { it.lowercase() }.distinct()
        if (keys.isEmpty()) return
        markSeenSeq++
        val fallbackSeenAt = maxOf(
            System.currentTimeMillis(),
            _claudeJobsForLabels.value.filter { it.itemUuid.lowercase() in keys }.maxOfOrNull { it.completedAt } ?: 0L
        )
        _claudeLabelSeenAt.value = _claudeLabelSeenAt.value + keys.associateWith { fallbackSeenAt }
        val url = pcServerUrl()
        val token = pcAuthToken()
        if (url.isBlank()) return
        scope.launch(Dispatchers.IO) {
            val resp = try { pcServerClient.postClaudeLabelSeen(itemUuids.filter { it.isNotBlank() }.distinct(), url, token) } catch (_: Exception) { null }
            if (resp != null && resp.status == "ok" && resp.seenAt > 0) {
                claudeRefreshMutex.withLock {
                    _claudeLabelSeenAt.value = _claudeLabelSeenAt.value + keys.associateWith { resp.seenAt }
                    if (resp.labelSeenRevision > lastKnownLabelSeenRevision) {
                        lastKnownLabelSeenRevision = resp.labelSeenRevision
                    }
                }
            }
        }
    }

    private suspend fun resolveClaudeApprovalThumbnails(pending: List<ClaudeJobDto>): Map<String, String> {
        val result = mutableMapOf<String, String>()
        for (job in pending) {
            try {
                var path: String? = null
                val md5 = job.photos.firstOrNull()?.md5 ?: ""
                if (md5.isNotBlank()) {
                    val photo = repository.getPhotoByMd5(md5)
                    if (photo != null) {
                        path = listOfNotNull(photo.thumbnailPath, photo.filePath)
                            .firstOrNull { it.isNotBlank() && File(it).exists() }
                    }
                }
                if (path == null && job.itemUuid.isNotBlank()) {
                    val withPhotos = repository.getItemWithPhotosBySyncUuid(job.itemUuid)
                    val first = withPhotos?.photos?.minByOrNull { it.displayOrder }
                    if (first != null) {
                        path = listOfNotNull(first.thumbnailPath, first.filePath)
                            .firstOrNull { it.isNotBlank() && File(it).exists() }
                    }
                }
                if (!path.isNullOrBlank()) {
                    result[job.jobId] = path
                }
            } catch (_: Exception) {
            }
        }
        return result
    }

    fun respondToClaudeApprovals(checkedIds: Set<String>, displayedIds: List<String>) {
        val url = pcServerUrl()
        val token = pcAuthToken()
        if (url.isBlank()) return
        if (_claudeActionInProgress.value) return
        _claudeActionInProgress.value = true
        scope.launch(Dispatchers.IO) {
            try {
                var approvedCount = 0
                var approveFailed = false
                for (jobId in displayedIds) {
                    if (!checkedIds.contains(jobId)) continue
                    val ok = pcServerClient.postClaudeApproval(jobId, true, url, token)
                    if (ok) {
                        approvedCount++
                    } else {
                        approveFailed = true
                        break
                    }
                }
                var movedCount = 0
                var moveFailedCount = 0
                if (!approveFailed) {
                    for (jobId in displayedIds) {
                        if (checkedIds.contains(jobId)) continue
                        val ok = pcServerClient.postClaudeJobMove(jobId, "waiting", url, token)
                        if (ok) {
                            movedCount++
                        } else {
                            moveFailedCount++
                        }
                    }
                }
                try {
                    refreshClaudeJobStates(url, token)
                } catch (_: Exception) {
                }
                if (!approveFailed && moveFailedCount == 0 && _pendingClaudeApprovals.value.isEmpty()) {
                    _dismissedClaudeApprovalIds.value = emptySet()
                    _claudeApprovalPeekRequested.value = false
                }
                val messages = mutableListOf<String>()
                if (approvedCount > 0) {
                    messages.add("${approvedCount}件を承認しました")
                }
                if (approveFailed) {
                    messages.add("途中で送信に失敗したため停止しました（残りは承認待ちのままです）")
                } else {
                    if (movedCount > 0) {
                        messages.add("${movedCount}件を待機へ戻しました")
                    }
                    if (moveFailedCount > 0) {
                        messages.add("${moveFailedCount}件を待機へ戻せませんでした")
                    }
                }
                if (messages.isNotEmpty()) {
                    _claudeActionMessage.value = messages.joinToString("。")
                }
            } finally {
                _claudeActionInProgress.value = false
            }
        }
    }

    fun acknowledgeClaudeCompletion(jobId: String) {
        val url = pcServerUrl()
        val token = pcAuthToken()
        if (url.isBlank()) return
        if (_claudeActionInProgress.value) return
        _claudeActionInProgress.value = true
        scope.launch(Dispatchers.IO) {
            try {
                val ok = pcServerClient.postClaudeJobAck(jobId, url, token)
                if (ok) {
                    _unackedClaudeCompletions.value = _unackedClaudeCompletions.value.filter { it.jobId != jobId }
                    try {
                        refreshClaudeJobStates(url, token)
                    } catch (_: Exception) {
                    }
                } else {
                    try {
                        refreshClaudeJobStates(url, token)
                    } catch (_: Exception) {
                    }
                    if (_unackedClaudeCompletions.value.any { it.jobId == jobId }) {
                        _claudeActionMessage.value = "PCサーバーへ確認を送信できませんでした。接続を確認してもう一度お試しください。"
                    }
                }
            } finally {
                _claudeActionInProgress.value = false
            }
        }
    }

    fun startClaudeWatchLoop() {
        claudeWatchJob?.cancel()
        claudeWatchJob = scope.launch(Dispatchers.IO) {
            while (isActive) {
                val url = pcServerUrl()
                val token = pcAuthToken()
                val heartbeatRecent = (System.currentTimeMillis() - lastHeartbeatResponseAtMs()) < 5500L
                if (url.isNotBlank() && !_claudeActionInProgress.value && !heartbeatRecent) {
                    try {
                        val revision = pcServerClient.fetchClaudeRevision(url, token)
                        if (revision != null && revision > 0L && revision != lastKnownClaudeRevision) {
                            refreshClaudeJobStates(url, token, fromWatch = true)
                        }
                    } catch (e: Exception) {
                        if (e is CancellationException) throw e
                        android.util.Log.e("PC_SYNC", "Claudeジョブ監視に失敗: ${e.message}")
                    }
                }
                delay(4000)
            }
        }
    }

    fun resetForServerChange() {
        lastKnownClaudeRevision = -1L
        _pendingClaudeApprovals.value = emptyList()
        _pendingClaudeApprovalThumbnails.value = emptyMap()
        _dismissedClaudeApprovalIds.value = emptySet()
        _claudeApprovalPeekRequested.value = false
        _unackedClaudeCompletions.value = emptyList()
        _dismissedClaudeCompletionIds.value = emptySet()
        lastKnownLabelSeenRevision = 0L
        _claudeJobsForLabels.value = emptyList()
        _claudeLabelSeenAt.value = emptyMap()
        _labelVisitSnapshot.value = emptyMap()
        _labelBundleUuids.value = emptyList()
    }

    fun onCleared() {
        claudeWatchJob?.cancel()
    }
}
