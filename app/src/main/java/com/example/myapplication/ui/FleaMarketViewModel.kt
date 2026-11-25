package com.example.myapplication.ui

import android.app.Application
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.myapplication.data.BatchEditSpec
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.local.SalesSummary
import com.example.myapplication.data.server.ClaudeJobDto
import com.example.myapplication.data.server.ClaudeReferenceDto
import com.example.myapplication.data.server.LocalWebServer
import com.example.myapplication.data.server.NetworkUtils
import com.example.myapplication.data.server.PcServerClient
import com.example.myapplication.data.server.PcSyncProgress
import com.example.myapplication.data.server.PcSyncResult
import com.example.myapplication.data.server.SyncCoordinator
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File

class FleaMarketViewModel(application: Application) : AndroidViewModel(application) {
    data class ClaudeItemLabelSummary(val key: String, val count: Int)

    private val syncCoordinator = SyncCoordinator.getInstance(application)
    private val repository: FleaMarketRepository = syncCoordinator.repository
    private val pcServerClient = syncCoordinator.pcServerClient
    private val webServer = LocalWebServer(application, repository)

    private lateinit var pcSyncController: PcSyncController
    private val claudeController = ClaudeWorkflowController(
        scope = viewModelScope,
        repository = repository,
        pcServerClient = pcServerClient,
        pcServerUrl = { pcSyncController.pcServerUrl.value },
        pcAuthToken = { pcSyncController.pcAuthToken.value },
        isPcServerConnected = { syncCoordinator.isPcServerConnected.value },
        lastHeartbeatResponseAtMs = { pcSyncController.lastHeartbeatResponseAtMs },
        onTemplateRegisteredRefreshRevisions = { url, token ->
            pcSyncController.refreshKnownRevisions(url, token)
        }
    )

    init {
        pcSyncController = PcSyncController(
            application = application,
            scope = viewModelScope,
            syncCoordinator = syncCoordinator,
            repository = repository,
            claudeController = claudeController
        )
    }

    private val maintenanceHelper = MaintenanceExportHelper(
        application = application,
        repository = repository,
        pcServerClient = pcServerClient,
        pcServerUrl = { pcSyncController.pcServerUrl.value },
        pcAuthToken = { pcSyncController.pcAuthToken.value },
        isPcServerConnected = { syncCoordinator.isPcServerConnected.value }
    )

    val isWebServerRunning: StateFlow<Boolean> = webServer.isRunning
    val webServerLogs: StateFlow<List<String>> = webServer.logs
    val webServerToken: StateFlow<String> = webServer.accessToken
    val serverPort: Int = webServer.port

    private val _serverIpAddress = MutableStateFlow<String?>(null)
    val serverIpAddress: StateFlow<String?> = _serverIpAddress.asStateFlow()

    val defaultSettings: StateFlow<DefaultSettings> = repository.defaultSettings

    val itemsWithPhotos: StateFlow<List<ItemWithPhotos>> = repository.getAllActiveItemsWithPhotos()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val trashedItemsWithPhotos: StateFlow<List<ItemWithPhotos>> = repository.getTrashedItemsWithPhotos()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val trashCount: StateFlow<Int> = repository.getTrashedCount()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), 0)

    val allSalesSummary: StateFlow<SalesSummary> = repository.getSalesSummary()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), SalesSummary())

    private val _searchQuery = MutableStateFlow("")
    val searchQuery: StateFlow<String> = _searchQuery.asStateFlow()

    private val _statusFilter = MutableStateFlow("ALL")
    val statusFilter: StateFlow<String> = _statusFilter.asStateFlow()

    private val _sortOption = MutableStateFlow(SortOption.DATE_DESC)
    val sortOption: StateFlow<SortOption> = _sortOption.asStateFlow()

    private val _timeRange = MutableStateFlow(TimeRange.ALL_TIME)
    val timeRange: StateFlow<TimeRange> = _timeRange.asStateFlow()

    private val _isSelectionMode = MutableStateFlow(false)
    val isSelectionMode: StateFlow<Boolean> = _isSelectionMode.asStateFlow()

    private val _selectedItemIds = MutableStateFlow<Set<Int>>(emptySet())
    val selectedItemIds: StateFlow<Set<Int>> = _selectedItemIds.asStateFlow()

    val missingOriginalItems: StateFlow<List<ItemWithPhotos>> = itemsWithPhotos.combine(defaultSettings) { items, _ ->
        items.filter { itemWithPhotos -> itemWithPhotos.photos.any { it.isOriginalDeleted } }
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val unsyncedItemsCount: StateFlow<Int> = itemsWithPhotos.combine(defaultSettings) { items, _ ->
        items.count { itemWithPhotos ->
            itemWithPhotos.photos.any { !it.isPcSynced && File(it.filePath).exists() }
        }
    }.flowOn(Dispatchers.IO).stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), 0)

    val missingOriginalCount: StateFlow<Int> = missingOriginalItems.combine(defaultSettings) { items, _ ->
        items.size
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), 0)

    val filteredItems: StateFlow<List<ItemWithPhotos>> = combine(
        itemsWithPhotos, searchQuery, statusFilter, sortOption, defaultSettings
    ) { items, query, status, sort, settings ->
        ItemFilterSorter.filterAndSortItems(items, query, status, sort, settings)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val periodSalesSummary: StateFlow<SalesSummary> = combine(
        itemsWithPhotos, defaultSettings, timeRange
    ) { items, settings, range ->
        ItemFilterSorter.computePeriodSalesSummary(items, settings, range)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), SalesSummary())

    val pcServerUrl: StateFlow<String> = pcSyncController.pcServerUrl
    val pcAuthToken: StateFlow<String> = pcSyncController.pcAuthToken
    val isPcServerConnected: StateFlow<Boolean> = pcSyncController.isPcServerConnected
    val syncStatusState: StateFlow<PcSyncProgress> = pcSyncController.syncStatusState
    val lastSyncSuccessAt: StateFlow<Long?> = pcSyncController.lastSyncSuccessAt

    val pendingClaudeApprovals: StateFlow<List<ClaudeJobDto>> = claudeController.pendingClaudeApprovals
    val claudeJobsForLabels: StateFlow<List<ClaudeJobDto>> = claudeController.claudeJobsForLabels
    val claudeLabelSeenAt: StateFlow<Map<String, Long>> = claudeController.claudeLabelSeenAt
    val labelVisitSnapshot: StateFlow<Map<String, Long>> = claudeController.labelVisitSnapshot
    val claudeItemLabelSummaries: StateFlow<Map<Int, ClaudeItemLabelSummary>> = claudeController.claudeItemLabelSummaries
    val labelBundleUuids: StateFlow<List<String>> = claudeController.labelBundleUuids
    val dismissedClaudeApprovalIds: StateFlow<Set<String>> = claudeController.dismissedClaudeApprovalIds
    val pendingClaudeApprovalThumbnails: StateFlow<Map<String, String>> = claudeController.pendingClaudeApprovalThumbnails
    val claudeApprovalPeekRequested: StateFlow<Boolean> = claudeController.claudeApprovalPeekRequested
    val unackedClaudeCompletions: StateFlow<List<ClaudeJobDto>> = claudeController.unackedClaudeCompletions
    val dismissedClaudeCompletionIds: StateFlow<Set<String>> = claudeController.dismissedClaudeCompletionIds
    val claudeActionInProgress: StateFlow<Boolean> = claudeController.claudeActionInProgress
    val claudeActionMessage: StateFlow<String?> = claudeController.claudeActionMessage

    init {
        refreshServerIp()
        viewModelScope.launch(Dispatchers.IO) {
            repository.getPhotoStorageManager().cleanupTemporaryCaches()
            repository.purgeExpiredTrash(30)
            repository.seedInitialTestDataIfNeeded()
        }
        pcSyncController.startHeartbeatLoop()
        claudeController.startClaudeWatchLoop()
    }

    fun refreshServerIp() {
        _serverIpAddress.value = NetworkUtils.getLocalIpAddress()
    }

    fun toggleWebServer(enabled: Boolean) {
        if (enabled) {
            refreshServerIp()
            webServer.start()
        } else {
            webServer.stop()
        }
    }

    fun getMissingOriginalExportText(): String =
        ItemFilterSorter.buildMissingOriginalExportText(missingOriginalItems.value)

    fun setSearchQuery(query: String) { _searchQuery.value = query }
    fun setStatusFilter(status: String) { _statusFilter.value = status }
    fun setSortOption(option: SortOption) { _sortOption.value = option }
    fun setTimeRange(range: TimeRange) { _timeRange.value = range }

    fun toggleSelectionMode() {
        _isSelectionMode.value = !_isSelectionMode.value
        if (!_isSelectionMode.value) _selectedItemIds.value = emptySet()
    }

    fun toggleItemSelection(id: Int) {
        val current = _selectedItemIds.value.toMutableSet()
        if (current.contains(id)) {
            current.remove(id)
        } else {
            val itemWithPhotos = itemsWithPhotos.value.firstOrNull { it.item.id == id }
            if (itemWithPhotos != null && (itemWithPhotos.item.status == "MERGED" || itemWithPhotos.item.deletedAt != null)) {
                return
            }
            current.add(id)
        }
        _selectedItemIds.value = current
    }

    fun clearSelection() {
        _selectedItemIds.value = emptySet()
        _isSelectionMode.value = false
    }

    fun updateDefaultSettings(newSettings: DefaultSettings) {
        repository.updateDefaultSettings(newSettings, stampNow = true)
        val url = pcServerUrl.value
        val token = pcAuthToken.value
        if (url.isNotBlank() && isPcServerConnected.value) {
            viewModelScope.launch(Dispatchers.IO) {
                syncCoordinator.syncMutex.withLock {
                    if (pcServerClient.pushSettingsToServer(url, token)) {
                        pcSyncController.refreshKnownRevisions(url, token)
                    }
                }
            }
        }
    }

    suspend fun getItemById(id: Int): ItemWithPhotos? = repository.getItemById(id)

    fun claudeFreeTextHistory(): List<String> = claudeController.claudeFreeTextHistory()

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
    ) = claudeController.sendPhotosToClaude(
        itemId, itemUuid, photos, templateId, queue, promptText, customPromptName,
        registerTemplateName, referenceMd5, stampNumber, referenceFileName, onError, onSuccess
    )

    suspend fun fetchClaudeReferences(): List<ClaudeReferenceDto>? = claudeController.fetchClaudeReferences()
    fun claudeReferenceImageUrl(md5: String, thumb: Boolean): Pair<String, String>? =
        claudeController.claudeReferenceImageUrl(md5, thumb)

    suspend fun getSelectedItems(): List<ItemWithPhotos> =
        repository.getDao().getItemsWithPhotosByIds(_selectedItemIds.value.toList())

    fun setItemEditingActive(itemId: Int, active: Boolean) = repository.setItemEditingActive(itemId, active)

    fun saveItem(
        id: Int,
        title: String,
        description: String,
        status: String,
        price: Int,
        soldPrice: Int,
        shippingCost: Int,
        feeRatePercent: Int,
        expenseCost: Int,
        useCustomSettings: Boolean,
        imageUris: List<Uri>,
        existingFilePaths: List<String>,
        onSkippedInvalidPhotos: (Int) -> Unit = {},
        onError: (String) -> Unit = {},
        onSuccess: () -> Unit
    ) {
        viewModelScope.launch {
            try {
                var skippedCount = 0
                repository.saveItem(
                    id = id, title = title, description = description, status = status,
                    price = price, soldPrice = soldPrice, shippingCost = shippingCost,
                    feeRatePercent = feeRatePercent, expenseCost = expenseCost,
                    useCustomSettings = useCustomSettings, imageUris = imageUris,
                    existingFilePaths = existingFilePaths,
                    onSkippedInvalidPhotos = { skippedCount = it }
                )
                if (skippedCount > 0) onSkippedInvalidPhotos(skippedCount)
                triggerAutoSync()
                onSuccess()
            } catch (e: Exception) {
                onError(e.localizedMessage ?: e.message ?: "保存中にエラーが発生しました")
            }
        }
    }

    fun removePhotoFromItem(itemId: Int, photoId: Int, onResult: (Boolean) -> Unit = {}) {
        viewModelScope.launch {
            val removed = withContext(Dispatchers.IO) { repository.removePhotoFromItem(itemId, photoId) }
            if (removed) triggerAutoSync()
            onResult(removed)
        }
    }

    fun mergeSelectedItems(
        mergedTitle: String,
        mergedDescription: String,
        totalSoldPrice: Int,
        newShippingCost: Int,
        newFeeRatePercent: Int,
        newExpenseCost: Int,
        childItemIds: List<Int> = _selectedItemIds.value.toList(),
        asStatus: String = "LISTED",
        onSuccess: (Long) -> Unit
    ) {
        viewModelScope.launch {
            val ids = childItemIds.ifEmpty { _selectedItemIds.value.toList() }
            val newId = repository.mergeItems(
                mergedTitle = mergedTitle, mergedDescription = mergedDescription,
                totalSoldPrice = totalSoldPrice, newShippingCost = newShippingCost,
                newFeeRatePercent = newFeeRatePercent, newExpenseCost = newExpenseCost,
                childItemIds = ids, asStatus = asStatus
            )
            clearSelection()
            triggerAutoSync()
            onSuccess(newId)
        }
    }

    fun unmergeItem(parentId: Int, onSuccess: () -> Unit = {}) {
        viewModelScope.launch {
            repository.unmergeItem(parentId)
            triggerAutoSync()
            onSuccess()
        }
    }

    suspend fun getChildItems(parentId: Int): List<ItemWithPhotos> =
        repository.getMergedChildItemsWithPhotos(parentId)

    fun updateItemStatus(item: ItemEntity, newStatus: String) {
        viewModelScope.launch {
            repository.updateItemStatus(item, newStatus)
            triggerAutoSync()
        }
    }

    fun deleteItem(itemWithPhotos: ItemWithPhotos, onSuccess: () -> Unit) {
        viewModelScope.launch {
            repository.moveToTrash(itemWithPhotos)
            triggerAutoSync()
            onSuccess()
        }
    }

    fun restoreFromTrash(itemId: Int, onSuccess: () -> Unit = {}) {
        viewModelScope.launch {
            repository.restoreFromTrash(itemId)
            triggerAutoSync()
            onSuccess()
        }
    }

    fun permanentlyDeleteItem(itemWithPhotos: ItemWithPhotos, onSuccess: () -> Unit = {}) {
        viewModelScope.launch {
            repository.permanentlyDeleteItem(itemWithPhotos, recordTombstoneForSync = true)
            triggerAutoSync()
            onSuccess()
        }
    }

    fun emptyTrash(onSuccess: (Int) -> Unit = {}) {
        viewModelScope.launch {
            val count = repository.emptyTrash()
            triggerAutoSync()
            onSuccess(count)
        }
    }

    fun batchMoveToTrash(itemIds: List<Int>, onSuccess: (Int) -> Unit = {}) {
        viewModelScope.launch {
            val count = repository.moveItemsToTrash(itemIds)
            clearSelection()
            if (count > 0) triggerAutoSync()
            onSuccess(count)
        }
    }

    fun batchUpdateItems(itemIds: List<Int>, spec: BatchEditSpec, onSuccess: (Int) -> Unit = {}) {
        viewModelScope.launch {
            val count = repository.batchUpdateItems(itemIds, spec)
            clearSelection()
            if (count > 0) triggerAutoSync()
            onSuccess(count)
        }
    }

    fun batchRestoreFromTrash(itemIds: List<Int>, onSuccess: (Int) -> Unit = {}) {
        viewModelScope.launch {
            val count = repository.restoreItemsFromTrash(itemIds)
            if (count > 0) triggerAutoSync()
            onSuccess(count)
        }
    }

    fun batchPermanentlyDeleteFromTrash(itemIds: List<Int>, onSuccess: (Int) -> Unit = {}) {
        viewModelScope.launch {
            val count = repository.permanentlyDeleteItemsFromTrash(itemIds)
            if (count > 0) triggerAutoSync()
            onSuccess(count)
        }
    }

    fun findCachedOriginalPhotoFile(md5Hash: String): File? = maintenanceHelper.findCachedOriginalPhotoFile(md5Hash)
    suspend fun fetchOriginalPhotoCacheFile(md5Hash: String): File? = maintenanceHelper.fetchOriginalPhotoCacheFile(md5Hash)

    fun shareItemWithZip(context: Context, itemWithPhotos: ItemWithPhotos) {
        viewModelScope.launch {
            val item = itemWithPhotos.item
            val photos = itemWithPhotos.photos.mapNotNull { it.displayFile?.absolutePath }
            if (photos.isEmpty() && item.description.isBlank()) {
                Toast.makeText(context, "共有できる写真または説明文がありません", Toast.LENGTH_SHORT).show()
                return@launch
            }
            val shareIntent = maintenanceHelper.createZipShareIntent(itemWithPhotos)
            if (shareIntent != null) {
                val chooser = Intent.createChooser(shareIntent, "他アプリでZIP共有 (LocalSend / QuickShare等)")
                chooser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                context.startActivity(chooser)
            } else {
                Toast.makeText(context, "ZIPファイルの生成に失敗しました", Toast.LENGTH_SHORT).show()
            }
        }
    }

    suspend fun repairItemPhotosIfNeeded(itemId: Int): Boolean {
        if (!isPcServerConnected.value) return false
        val url = pcServerUrl.value
        val token = pcAuthToken.value
        if (url.isBlank()) return false
        return pcServerClient.repairMissingOrCorruptThumbnailsForItem(itemId, url, token) > 0
    }

    fun exportSelectedPhotosToGallery(
        itemTitle: String,
        selectedPhotos: List<PhotoEntity>,
        fetchOriginalsFromPc: Boolean,
        onResult: (GalleryExportResult) -> Unit
    ) {
        viewModelScope.launch {
            onResult(maintenanceHelper.exportSelectedPhotosToGallery(itemTitle, selectedPhotos, fetchOriginalsFromPc))
        }
    }

    fun exportItemPhotosToGallery(
        context: Context,
        itemTitle: String,
        photoPaths: List<String>,
        onResult: (Int) -> Unit
    ) {
        viewModelScope.launch {
            onResult(maintenanceHelper.exportItemPhotosToGallery(itemTitle, photoPaths))
        }
    }

    fun exportAllToCsv(context: Context) {
        viewModelScope.launch {
            val shareIntent = maintenanceHelper.exportAllToCsvIntent(context, itemsWithPhotos.value, defaultSettings.value)
            if (shareIntent != null) {
                val chooser = Intent.createChooser(shareIntent, "CSVを外部アプリ（Excel/スプレッドシート等）で開く")
                chooser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                context.startActivity(chooser)
            } else {
                Toast.makeText(context, "CSVの作成に失敗しました", Toast.LENGTH_SHORT).show()
            }
        }
    }

    fun copyListingTemplate(context: Context, itemWithPhotos: ItemWithPhotos) =
        maintenanceHelper.copyListingTemplate(context, itemWithPhotos)

    fun cleanupSharedOriginalPhotos(onResult: (Int) -> Unit) {
        viewModelScope.launch {
            onResult(maintenanceHelper.cleanupSharedOriginalPhotos(itemsWithPhotos.value))
        }
    }

    fun cleanupSoldPhotosOlderThan(days: Int, onResult: (Int) -> Unit) {
        viewModelScope.launch {
            onResult(maintenanceHelper.cleanupSoldPhotosOlderThan(itemsWithPhotos.value, days))
        }
    }

    fun refreshClaudeItemLabels() = claudeController.refreshClaudeItemLabels()
    fun clearClaudeActionMessage() = claudeController.clearClaudeActionMessage()
    fun dismissClaudeApprovalForNow() = claudeController.dismissClaudeApprovalForNow()
    fun requestClaudeApprovalPeek() = claudeController.requestClaudeApprovalPeek()
    fun dismissClaudeCompletionForNow(jobId: String) = claudeController.dismissClaudeCompletionForNow(jobId)

    fun claudePhotoLabelKey(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobs: List<ClaudeJobDto>,
        seenAtMap: Map<String, Long>,
        snapshotMap: Map<String, Long>
    ): String? = claudeController.claudePhotoLabelKey(itemUuid, bundleUuids, md5, jobs, seenAtMap, snapshotMap)

    fun claudePhotoLabelSourceJob(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobs: List<ClaudeJobDto>
    ): ClaudeJobDto? = claudeController.claudePhotoLabelSourceJob(itemUuid, bundleUuids, md5, jobs)

    fun cancelClaudeJobFromLabel(itemUuid: String, bundleUuids: List<String>, md5: String, jobId: String) =
        claudeController.cancelClaudeJobFromLabel(itemUuid, bundleUuids, md5, jobId)

    fun beginLabelVisit(itemId: Int, itemUuid: String) = claudeController.beginLabelVisit(itemId, itemUuid)
    fun endLabelVisit() = claudeController.endLabelVisit()
    fun markClaudeLabelSeen(itemUuids: List<String>) = claudeController.markClaudeLabelSeen(itemUuids)
    fun respondToClaudeApprovals(checkedIds: Set<String>, displayedIds: List<String>) =
        claudeController.respondToClaudeApprovals(checkedIds, displayedIds)
    fun acknowledgeClaudeCompletion(jobId: String) = claudeController.acknowledgeClaudeCompletion(jobId)

    fun triggerAutoSync() = pcSyncController.triggerAutoSync()
    fun updatePcServerUrl(url: String) = pcSyncController.updatePcServerUrl(url)
    fun updatePcAuthToken(token: String) = pcSyncController.updatePcAuthToken(token)
    fun parseQrCodeText(qrText: String): Boolean = pcSyncController.parseQrCodeText(qrText)
    fun testPcServerConnection(onResult: (Boolean) -> Unit) = pcSyncController.testPcServerConnection(onResult)
    fun syncDeltaToPcServer(onResult: (PcSyncResult) -> Unit) = pcSyncController.syncDeltaToPcServer(onResult)

    override fun onCleared() {
        super.onCleared()
        pcSyncController.onCleared()
        claudeController.onCleared()
        webServer.stop()
    }
}
