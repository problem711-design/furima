package com.example.myapplication.ui

import android.app.Application
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import java.io.File
import com.example.myapplication.data.CsvExporter
import com.example.myapplication.data.BatchEditSpec
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.isSoldFamilyStatus
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.local.SalesSummary
import com.example.myapplication.data.server.LocalWebServer
import com.example.myapplication.data.server.NetworkUtils
import com.example.myapplication.data.server.PcSyncProgress
import com.example.myapplication.data.server.SyncPhase
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
import kotlinx.coroutines.withContext
import java.util.Calendar

enum class SortOption(val label: String) {
    DATE_DESC("出品が新しい順"),
    DATE_ASC("出品が古い順"),
    SOLD_DATE_DESC("売却日が新しい順"),
    PRICE_DESC("価格が高い順"),
    PRICE_ASC("価格が安い順"),
    PROFIT_DESC("純利益が高い順")
}

enum class TimeRange(val label: String) {
    THIS_WEEK("今週"),
    THIS_MONTH("今月"),
    ALL_TIME("全期間")
}

data class GalleryExportResult(
    val savedCount: Int = 0,
    val fetchedFromServerCount: Int = 0,
    val fallbackLocalCount: Int = 0,
    val usedThumbnailOnlyCount: Int = 0
)

class FleaMarketViewModel(application: Application) : AndroidViewModel(application) {
    private val repository = FleaMarketRepository(application)
    private val webServer = LocalWebServer(application, repository)

    val isWebServerRunning: StateFlow<Boolean> = webServer.isRunning
    val webServerLogs: StateFlow<List<String>> = webServer.logs
    val serverPort: Int = webServer.port

    private val _serverIpAddress = MutableStateFlow<String?>(null)
    val serverIpAddress: StateFlow<String?> = _serverIpAddress.asStateFlow()

    init {
        refreshServerIp()
        viewModelScope.launch(Dispatchers.IO) {
            repository.getPhotoStorageManager().cleanupTemporaryCaches()
            repository.purgeExpiredTrash(30)
            repository.seedInitialTestDataIfNeeded()
        }
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

    val defaultSettings: StateFlow<DefaultSettings> = repository.defaultSettings

    val itemsWithPhotos: StateFlow<List<ItemWithPhotos>> = repository.getAllActiveItemsWithPhotos()
        .stateIn(
            scope = viewModelScope,
            started = SharingStarted.WhileSubscribed(5000),
            initialValue = emptyList()
        )

    val trashedItemsWithPhotos: StateFlow<List<ItemWithPhotos>> = repository.getTrashedItemsWithPhotos()
        .stateIn(
            scope = viewModelScope,
            started = SharingStarted.WhileSubscribed(5000),
            initialValue = emptyList()
        )

    val trashCount: StateFlow<Int> = repository.getTrashedCount()
        .stateIn(
            scope = viewModelScope,
            started = SharingStarted.WhileSubscribed(5000),
            initialValue = 0
        )

    val allSalesSummary: StateFlow<SalesSummary> = repository.getSalesSummary()
        .stateIn(
            scope = viewModelScope,
            started = SharingStarted.WhileSubscribed(5000),
            initialValue = SalesSummary()
        )

    // 検索・絞り込み・ソート状態
    private val _searchQuery = MutableStateFlow("")
    val searchQuery: StateFlow<String> = _searchQuery.asStateFlow()

    private val _statusFilter = MutableStateFlow("ALL") // "ALL", "LISTED", "SOLD"
    val statusFilter: StateFlow<String> = _statusFilter.asStateFlow()

    private val _sortOption = MutableStateFlow(SortOption.DATE_DESC)
    val sortOption: StateFlow<SortOption> = _sortOption.asStateFlow()

    private val _timeRange = MutableStateFlow(TimeRange.ALL_TIME)
    val timeRange: StateFlow<TimeRange> = _timeRange.asStateFlow()

    // 複数選択（まとめ買い）状態
    private val _isSelectionMode = MutableStateFlow(false)
    val isSelectionMode: StateFlow<Boolean> = _isSelectionMode.asStateFlow()

    private val _selectedItemIds = MutableStateFlow<Set<Int>>(emptySet())
    val selectedItemIds: StateFlow<Set<Int>> = _selectedItemIds.asStateFlow()

    // 未同期・原本なしの統計 StateFlow
    val missingOriginalItems: StateFlow<List<ItemWithPhotos>> = itemsWithPhotos.combine(defaultSettings) { items, _ ->
        items.filter { itemWithPhotos ->
            itemWithPhotos.photos.any { it.isOriginalDeleted }
        }
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5000),
        initialValue = emptyList()
    )

    val unsyncedItemsCount: StateFlow<Int> = itemsWithPhotos.combine(defaultSettings) { items, _ ->
        items.count { itemWithPhotos ->
            itemWithPhotos.photos.any { !it.isPcSynced && File(it.filePath).exists() }
        }
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5000),
        initialValue = 0
    )

    val missingOriginalCount: StateFlow<Int> = missingOriginalItems.combine(defaultSettings) { items, _ ->
        items.size
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5000),
        initialValue = 0
    )

    fun getMissingOriginalExportText(): String {
        val sb = java.lang.StringBuilder()
        sb.append("【送信済み写真一覧 (サムネイル化済)】\n")
        for (itemWithPhotos in missingOriginalItems.value) {
            sb.append("- 商品ID: ${itemWithPhotos.item.id} | タイトル: ${itemWithPhotos.item.title}\n")
            for (photo in itemWithPhotos.photos) {
                if (photo.isOriginalDeleted) {
                    sb.append("  写真ID: ${photo.id}, MD5: ${photo.md5Hash.ifBlank { "なし" }}\n")
                }
            }
        }
        return sb.toString()
    }

    // フィルター＆ソート済みアイテム一覧
    val filteredItems: StateFlow<List<ItemWithPhotos>> = combine(
        itemsWithPhotos,
        searchQuery,
        statusFilter,
        sortOption
    ) { items, query, status, sort ->
        items.filter { itemWithPhotos ->
            val item = itemWithPhotos.item
            val itemFullText = "${item.title} ${item.description}"
            val matchesQuery = matchesSearchQuery(itemFullText, query)

            val matchesStatus = when (status) {
                "LISTED" -> item.status == "LISTED"
                "SOLD" -> item.status == "SOLD"
                "SHIPPING" -> item.status == "SHIPPING"
                "DRAFT" -> item.status == "DRAFT" || item.status == "DRAFT_DONE"
                else -> true
            }

            matchesQuery && matchesStatus
        }.sortedWith { a, b ->
            when (sort) {
                SortOption.DATE_DESC -> b.item.createdAt.compareTo(a.item.createdAt)
                SortOption.DATE_ASC -> a.item.createdAt.compareTo(b.item.createdAt)
                SortOption.SOLD_DATE_DESC -> (b.item.soldAt ?: 0L).compareTo(a.item.soldAt ?: 0L)
                SortOption.PRICE_DESC -> {
                    val pB = if (isSoldFamilyStatus(b.item.status) && b.item.soldPrice > 0) b.item.soldPrice else b.item.price
                    val pA = if (isSoldFamilyStatus(a.item.status) && a.item.soldPrice > 0) a.item.soldPrice else a.item.price
                    pB.compareTo(pA)
                }
                SortOption.PRICE_ASC -> {
                    val pB = if (isSoldFamilyStatus(b.item.status) && b.item.soldPrice > 0) b.item.soldPrice else b.item.price
                    val pA = if (isSoldFamilyStatus(a.item.status) && a.item.soldPrice > 0) a.item.soldPrice else a.item.price
                    pA.compareTo(pB)
                }
                SortOption.PROFIT_DESC -> {
                    val settings = defaultSettings.value
                    b.item.getCalculatedProfit(settings).compareTo(a.item.getCalculatedProfit(settings))
                }
            }
        }
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5000),
        initialValue = emptyList()
    )

    private fun matchesSearchQuery(text: String, query: String): Boolean {
        val q = query.trim()
        if (q.isBlank()) return true
        val lowerText = text.lowercase()

        // 1. 完全一致フレーズ検索: "..." で囲まれている場合
        if (q.startsWith("\"") && q.endsWith("\"") && q.length > 2) {
            val exactPhrase = q.substring(1, q.length - 1).lowercase()
            return lowerText.contains(exactPhrase)
        }

        // 2. OR 検索: 大文字の " OR " または " | " で区切られている場合
        if (q.contains(" OR ") || q.contains(" | ")) {
            val terms = q.split(Regex(" OR | \\| ")).map { it.trim().lowercase() }.filter { it.isNotBlank() }
            return terms.any { lowerText.contains(it) }
        }

        // 3. AND 検索: 半角または全角スペースで区切られた全単語を含む
        val words = q.split(Regex("[\\s　]+")).map { it.trim().lowercase() }.filter { it.isNotBlank() }
        return words.all { lowerText.contains(it) }
    }

    // 期間別サマリー（今週・今月・全期間）
    val periodSalesSummary: StateFlow<SalesSummary> = combine(
        itemsWithPhotos,
        defaultSettings,
        timeRange
    ) { items, settings, range ->
        val now = Calendar.getInstance()
        val startOfPeriod = Calendar.getInstance().apply {
            when (range) {
                TimeRange.THIS_WEEK -> {
                    set(Calendar.DAY_OF_WEEK, firstDayOfWeek)
                    set(Calendar.HOUR_OF_DAY, 0)
                    set(Calendar.MINUTE, 0)
                    set(Calendar.SECOND, 0)
                    set(Calendar.MILLISECOND, 0)
                }
                TimeRange.THIS_MONTH -> {
                    set(Calendar.DAY_OF_MONTH, 1)
                    set(Calendar.HOUR_OF_DAY, 0)
                    set(Calendar.MINUTE, 0)
                    set(Calendar.SECOND, 0)
                    set(Calendar.MILLISECOND, 0)
                }
                TimeRange.ALL_TIME -> {
                    timeInMillis = 0L
                }
            }
        }.timeInMillis

        var totalSales = 0
        var totalProfit = 0
        var listedCount = 0
        var soldCount = 0

        for (itemWithPhotos in items) {
            val item = itemWithPhotos.item
            if (isSoldFamilyStatus(item.status)) {
                val targetTime = item.soldAt ?: item.createdAt
                if (targetTime >= startOfPeriod) {
                    soldCount++
                    val price = if (item.soldPrice > 0) item.soldPrice else item.price
                    totalSales += price
                    totalProfit += item.getCalculatedProfit(settings)
                }
            } else if (item.status == "LISTED") {
                if (item.createdAt >= startOfPeriod || range == TimeRange.ALL_TIME) {
                    listedCount++
                }
            }
        }

        SalesSummary(totalSales, totalProfit, listedCount, soldCount)
    }.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5000),
        initialValue = SalesSummary()
    )

    fun setSearchQuery(query: String) {
        _searchQuery.value = query
    }

    fun setStatusFilter(status: String) {
        _statusFilter.value = status
    }

    fun setSortOption(option: SortOption) {
        _sortOption.value = option
    }

    fun setTimeRange(range: TimeRange) {
        _timeRange.value = range
    }

    fun toggleSelectionMode() {
        _isSelectionMode.value = !_isSelectionMode.value
        if (!_isSelectionMode.value) {
            _selectedItemIds.value = emptySet()
        }
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
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isNotBlank() && _isPcServerConnected.value) {
            viewModelScope.launch(Dispatchers.IO) {
                syncMutex.withLock {
                    if (pcServerClient.pushSettingsToServer(url, token)) {
                        refreshKnownRevisions(url, token)
                    }
                }
            }
        }
    }

    suspend fun getItemById(id: Int): ItemWithPhotos? {
        return repository.getItemById(id)
    }

    // フェーズ 6-8: 手打ちプロンプト履歴 (端末ローカルのみ・直近 3 件)
    fun claudeFreeTextHistory(): List<String> = pcServerClient.getClaudeFreeTextHistory()

    // 「Museに送る」送信ラッパー (フェーズ 5-3)。photos は (md5Hash, fileName) のリスト。
    // フェーズ 6-8: promptText を渡すと手打ちジョブになり、registerTemplateName で登録も試みる。
    fun sendPhotosToClaude(
        itemId: Int,
        itemUuid: String,
        photos: List<Pair<String, String>>,
        templateId: String,
        queue: String = "patrol",
        promptText: String? = null,
        customPromptName: String? = null,
        registerTemplateName: String? = null,
        onError: (String) -> Unit = {},
        onSuccess: (com.example.myapplication.data.server.PcServerClient.ClaudeJobCreateResult) -> Unit
    ) {
        viewModelScope.launch {
            if (!_isPcServerConnected.value) {
                onError("PCサーバー未接続です")
                return@launch
            }
            val result = pcServerClient.createClaudeJob(
                itemId, itemUuid, photos, templateId, queue,
                promptText = promptText,
                customPromptName = customPromptName,
                registerTemplateName = registerTemplateName
            )
            if (result.success) {
                if (!promptText.isNullOrBlank()) {
                    pcServerClient.pushClaudeFreeTextHistory(promptText)
                }
                if (result.templateRegistered) {
                    // §40.4 / レビュー 67-2: 登録できたら設定を取り直してテンプレート一覧へ即反映する。
                    // 通常の同期 (push あり) は使わず、登録結果を消さない専用経路で取り込み、
                    // 完了を待ってから onSuccess を呼ぶ (直後の再表示と競合させない)
                    try {
                        pcServerClient.fetchServerSettingsAfterTemplateRegistration()
                    } catch (_: Exception) {
                    }
                }
                onSuccess(result)
            } else {
                onError(result.message)
            }
        }
    }

    suspend fun getSelectedItems(): List<ItemWithPhotos> {
        val ids = _selectedItemIds.value.toList()
        return repository.getDao().getItemsWithPhotosByIds(ids)
    }

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
        onError: (String) -> Unit = {},
        onSuccess: () -> Unit
    ) {
        viewModelScope.launch {
            try {
                repository.saveItem(
                    id = id,
                    title = title,
                    description = description,
                    status = status,
                    price = price,
                    soldPrice = soldPrice,
                    shippingCost = shippingCost,
                    feeRatePercent = feeRatePercent,
                    expenseCost = expenseCost,
                    useCustomSettings = useCustomSettings,
                    imageUris = imageUris,
                    existingFilePaths = existingFilePaths
                )
                triggerAutoSync()
                onSuccess()
            } catch (e: Exception) {
                onError(e.localizedMessage ?: e.message ?: "保存中にエラーが発生しました")
            }
        }
    }

    fun removePhotoFromItem(itemId: Int, photoId: Int, onResult: (Boolean) -> Unit = {}) {
        viewModelScope.launch {
            val removed = withContext(Dispatchers.IO) {
                repository.removePhotoFromItem(itemId, photoId)
            }
            if (removed) {
                triggerAutoSync()
            }
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
                mergedTitle = mergedTitle,
                mergedDescription = mergedDescription,
                totalSoldPrice = totalSoldPrice,
                newShippingCost = newShippingCost,
                newFeeRatePercent = newFeeRatePercent,
                newExpenseCost = newExpenseCost,
                childItemIds = ids,
                asStatus = asStatus
            )
            clearSelection()
            triggerAutoSync()
            onSuccess(newId)
        }
    }

    /**
     * まとめ買い出品を解除（バラす）して元の子商品を出品中に復元
     */
    fun unmergeItem(parentId: Int, onSuccess: () -> Unit = {}) {
        viewModelScope.launch {
            repository.unmergeItem(parentId)
            triggerAutoSync()
            onSuccess()
        }
    }

    suspend fun getChildItems(parentId: Int): List<ItemWithPhotos> {
        return repository.getMergedChildItemsWithPhotos(parentId)
    }

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

    fun findCachedOriginalPhotoFile(md5Hash: String): File? {
        val cleanHash = md5Hash.trim().lowercase()
        if (cleanHash.isBlank()) return null
        val cacheDir = File(getApplication<Application>().cacheDir, "preview_originals")
        val target = File(cacheDir, "$cleanHash.jpg")
        return target.takeIf { it.exists() && it.length() > 0L }
    }

    suspend fun fetchOriginalPhotoCacheFile(md5Hash: String): File? = withContext(Dispatchers.IO) {
        val cleanHash = md5Hash.trim().lowercase()
        if (cleanHash.isBlank()) return@withContext null
        val existing = findCachedOriginalPhotoFile(cleanHash)
        if (existing != null) return@withContext existing

        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank() || !_isPcServerConnected.value) return@withContext null

        val cacheDir = File(getApplication<Application>().cacheDir, "preview_originals").apply { mkdirs() }
        val outFile = File(cacheDir, "$cleanHash.jpg")
        val ok = pcServerClient.downloadPhotoToFileByHash(url, token, cleanHash, outFile, verifyMd5 = true)
        if (ok) outFile.takeIf { it.exists() && it.length() > 0L } else null
    }

    /**
     * 他アプリZIP共有：LocalSend / QuickShare 等での単発共有専用（isShared 同期フラグは変更しない）
     */
    fun shareItemWithZip(context: Context, itemWithPhotos: ItemWithPhotos) {
        viewModelScope.launch {
            val item = itemWithPhotos.item
            val photos = itemWithPhotos.photos.mapNotNull { it.displayFile?.absolutePath }
            if (photos.isEmpty() && item.description.isBlank()) {
                Toast.makeText(context, "共有できる写真または説明文がありません", Toast.LENGTH_SHORT).show()
                return@launch
            }
            val shareIntent = withContext(Dispatchers.IO) {
                repository.getPhotoStorageManager().createStoredZipForSharing(
                    itemTitle = item.title,
                    description = item.description,
                    photoPaths = photos
                )
            }
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
        if (!_isPcServerConnected.value) return false
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
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
            val result = withContext(Dispatchers.IO) {
                val storageManager = repository.getPhotoStorageManager()
                val url = _pcServerUrl.value
                val token = _pcAuthToken.value
                val isOnline = _isPcServerConnected.value && url.isNotBlank()

                var saved = 0
                var fetchedFromServer = 0
                var fallbackLocal = 0
                var thumbOnly = 0

                selectedPhotos.forEachIndexed { index, photo ->
                    val origFile = if (!photo.isOriginalDeleted && photo.filePath.isNotBlank()) {
                        File(photo.filePath).takeIf {
                            it.exists() && !it.absolutePath.equals(photo.thumbnailPath, ignoreCase = true)
                        }
                    } else null

                    if (fetchOriginalsFromPc && isOnline) {
                        if (origFile != null) {
                            if (storageManager.syncFileToPublicGallery(itemTitle, origFile) != null) {
                                saved++
                            }
                        } else if (photo.md5Hash.isNotBlank()) {
                            val bytes = pcServerClient.downloadPhotoByHash(url, token, photo.md5Hash, verifyMd5 = true)
                            if (bytes != null) {
                                val fileName = "ORIG_${index + 1}_${photo.md5Hash.take(8)}.jpg"
                                if (storageManager.saveBytesToPublicGallery(itemTitle, fileName, bytes) != null) {
                                    saved++
                                    fetchedFromServer++
                                    return@forEachIndexed
                                }
                            }
                            val fallbackFile = photo.displayFile
                            if (fallbackFile != null && storageManager.syncFileToPublicGallery(itemTitle, fallbackFile) != null) {
                                saved++
                                fallbackLocal++
                                thumbOnly++
                            }
                        } else {
                            val fallbackFile = photo.displayFile
                            if (fallbackFile != null && storageManager.syncFileToPublicGallery(itemTitle, fallbackFile) != null) {
                                saved++
                                fallbackLocal++
                                if (photo.isOriginalDeleted || fallbackFile.absolutePath == photo.thumbnailPath) {
                                    thumbOnly++
                                }
                            }
                        }
                    } else {
                        val targetFile = origFile ?: photo.displayFile
                        if (targetFile != null && storageManager.syncFileToPublicGallery(itemTitle, targetFile) != null) {
                            saved++
                            if (origFile == null) {
                                thumbOnly++
                            }
                        }
                    }
                }

                GalleryExportResult(
                    savedCount = saved,
                    fetchedFromServerCount = fetchedFromServer,
                    fallbackLocalCount = fallbackLocal,
                    usedThumbnailOnlyCount = thumbOnly
                )
            }
            onResult(result)
        }
    }

    fun exportItemPhotosToGallery(
        context: Context,
        itemTitle: String,
        photoPaths: List<String>,
        onResult: (Int) -> Unit
    ) {
        viewModelScope.launch {
            val count = withContext(Dispatchers.IO) {
                repository.getPhotoStorageManager().exportPhotosToGallery(itemTitle, photoPaths)
            }
            onResult(count)
        }
    }

    fun exportAllToCsv(context: Context) {
        viewModelScope.launch {
            val items = itemsWithPhotos.value
            val settings = defaultSettings.value
            val shareIntent = withContext(Dispatchers.IO) {
                CsvExporter.exportAndShareCsv(context, items, settings)
            }
            if (shareIntent != null) {
                val chooser = Intent.createChooser(shareIntent, "CSVを外部アプリ（Excel/スプレッドシート等）で開く")
                chooser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                context.startActivity(chooser)
            } else {
                Toast.makeText(context, "CSVの作成に失敗しました", Toast.LENGTH_SHORT).show()
            }
        }
    }

    fun copyListingTemplate(context: Context, itemWithPhotos: ItemWithPhotos) {
        val item = itemWithPhotos.item
        val text = buildString {
            appendLine("【商品名】${item.title}")
            appendLine("【価格】¥${item.price}")
            if (item.description.isNotBlank()) {
                appendLine("")
                appendLine("【商品説明】")
                appendLine(item.description)
            }
            appendLine("")
            appendLine("ご覧いただきありがとうございます。即購入OKです。")
            appendLine("丁寧な梱包と迅速な発送を心がけております。よろしくお願いいたします。")
        }
        val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val clip = ClipData.newPlainText("出品テキスト", text)
        clipboard.setPrimaryClip(clip)
        Toast.makeText(context, "出品用テキストをクリップボードにコピーしました", Toast.LENGTH_SHORT).show()
    }

    /**
     * PC送信済みの元高解像度写真を削除（サムネイルのみ保持）
     */
    fun cleanupSharedOriginalPhotos(onResult: (Int) -> Unit) {
        viewModelScope.launch {
            val items = itemsWithPhotos.value.filter { it.item.isShared }
            var totalDeleted = 0
            withContext(Dispatchers.IO) {
                val storageManager = repository.getPhotoStorageManager()
                val dao = repository.getDao()
                val deletedOrigPaths = mutableSetOf<String>()
                for (itemWithPhotos in items) {
                    for (photo in itemWithPhotos.photos) {
                        val thumbPath = photo.thumbnailPath ?: continue
                        if (!File(thumbPath).exists()) continue
                        if (photo.filePath.isBlank() || photo.filePath.equals(thumbPath, ignoreCase = true)) continue
                        if (deletedOrigPaths.contains(photo.filePath)) {
                            dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                            continue
                        }
                        val origFile = File(photo.filePath)
                        if (origFile.exists()) {
                            if (photo.md5Hash.isBlank()) {
                                val md5 = storageManager.calculateMd5(origFile)
                                if (md5.isNotBlank()) {
                                    dao.updatePhotoMd5Hash(photo.id, md5)
                                }
                            }
                            if (origFile.delete()) {
                                deletedOrigPaths.add(photo.filePath)
                                totalDeleted++
                                dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                            }
                        } else if (!photo.isOriginalDeleted) {
                            dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                        }
                    }
                }
            }
            onResult(totalDeleted)
        }
    }

    /**
     * 売却済みから指定日数以上経過した写真をクリーンアップ
     */
    fun cleanupSoldPhotosOlderThan(days: Int, onResult: (Int) -> Unit) {
        viewModelScope.launch {
            val thresholdTime = System.currentTimeMillis() - (days * 24L * 60L * 60L * 1000L)
            val soldItems = itemsWithPhotos.value.filter {
                it.item.status == "SOLD" && (it.item.soldAt ?: it.item.createdAt) < thresholdTime
            }
            var totalDeleted = 0
            withContext(Dispatchers.IO) {
                for (itemWithPhotos in soldItems) {
                    val count = repository.getPhotoStorageManager().deletePhotoFiles(itemWithPhotos.photos)
                    totalDeleted += count
                }
            }
            onResult(totalDeleted)
        }
    }

    private val pcServerClient = com.example.myapplication.data.server.PcServerClient(getApplication(), repository)

    private val _pcServerUrl = MutableStateFlow<String>(
        pcServerClient.getSavedServerUrl()
    )
    val pcServerUrl: StateFlow<String> = _pcServerUrl.asStateFlow()

    private val _pcAuthToken = MutableStateFlow<String>(
        pcServerClient.getSavedAuthToken()
    )
    val pcAuthToken: StateFlow<String> = _pcAuthToken.asStateFlow()

    private val _isPcServerConnected = MutableStateFlow<Boolean>(false)
    val isPcServerConnected: StateFlow<Boolean> = _isPcServerConnected.asStateFlow()

    private val _syncStatusState = MutableStateFlow(PcSyncProgress())
    val syncStatusState: StateFlow<PcSyncProgress> = _syncStatusState.asStateFlow()

    private val _lastSyncSuccessAt = MutableStateFlow<Long?>(null)
    val lastSyncSuccessAt: StateFlow<Long?> = _lastSyncSuccessAt.asStateFlow()

    private var heartbeatJob: Job? = null
    private var claudeWatchJob: Job? = null
    // レビュー 24-4: Claude 状態の取得・反映専用の直列化 (商品同期の syncMutex とは別)
    private val claudeRefreshMutex = Mutex()
    private var autoSyncJob: Job? = null
    private var syncSuccessResetJob: Job? = null
    private val syncMutex = Mutex()
    @Volatile private var lastKnownDataRevision: Long = -1L
    @Volatile private var lastKnownSettingsRevision: Long = -1L
    @Volatile private var lastKnownClaudeRevision: Long = -1L

    // フェーズ 6-5 是正 (32-3): 既読専用カウンタの既知値 (claudeRevision とは別管理)
    @Volatile private var lastKnownLabelSeenRevision: Long = 0L

    // フェーズ 6-5 是正 (37-1): 自端末の既読化の世代カウンタ (取得中の競合判定に使う)
    @Volatile private var markSeenSeq: Long = 0L

    // フェーズ 5-4: 承認待ちジョブと未確認の完了ジョブ (ハートビート第3分岐で更新)
    // フェーズ 6-2: 承認待ちは単体から一覧 (createdAt 昇順) へ拡張した。
    private val _pendingClaudeApprovals = MutableStateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>>(emptyList())
    val pendingClaudeApprovals: StateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>> = _pendingClaudeApprovals.asStateFlow()

    // フェーズ 6-5: 加工ラベル導出用の全ジョブと既読マップ (refreshClaudeJobStates で更新)
    private val _claudeJobsForLabels = MutableStateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>>(emptyList())
    val claudeJobsForLabels: StateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>> = _claudeJobsForLabels.asStateFlow()
    private val _claudeLabelSeenAt = MutableStateFlow<Map<String, Long>>(emptyMap())
    val claudeLabelSeenAt: StateFlow<Map<String, Long>> = _claudeLabelSeenAt.asStateFlow()

    // フェーズ 6-5 是正 (34-2): 閲覧セッションのスナップショット (小文字 itemUuid -> 入室時点の既読時刻)。
    // 一覧→詳細→編集の遷移では上書きせず、一覧へ戻る/別商品へ移ると破棄する。
    private val _labelVisitSnapshot = MutableStateFlow<Map<String, Long>>(emptyMap())
    val labelVisitSnapshot: StateFlow<Map<String, Long>> = _labelVisitSnapshot.asStateFlow()

    // フェーズ 6-5 是正 (Build 13 / 49-1): 閲覧中商品のまとめ UUID 集合 (自分＋子孫・原表記)
    // フェーズ 6-9 (Build 14): 出品リスト用の商品単位ラベル集約。写真単位の導出 (claudePhotoLabelKey) の上に構築し、
    // リスト表示では閲覧スナップショットを使わず常に最新の既読時刻のみで評価する (§40.2 / §41.2 / 55-5)。
    data class ClaudeItemLabelSummary(val key: String, val count: Int)

    private val _claudeLabelTick = MutableStateFlow(0L)

    // 3 日窓の自然消滅に追随するため、一覧画面への復帰時に現在時刻で集約を再導出させる (§41.2)
    fun refreshClaudeItemLabels() {
        _claudeLabelTick.value = System.currentTimeMillis()
    }

    val claudeItemLabelSummaries: StateFlow<Map<Int, ClaudeItemLabelSummary>> =
        combine(
            repository.getAllItemsWithPhotos(),
            _claudeJobsForLabels,
            _claudeLabelSeenAt,
            _claudeLabelTick
        ) { allItems, jobs, seenAt, _ ->
            buildClaudeItemLabelSummaries(allItems, jobs, seenAt)
        }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyMap())

    private fun buildClaudeItemLabelSummaries(
        allItems: List<ItemWithPhotos>,
        jobs: List<com.example.myapplication.data.server.ClaudeJobDto>,
        seenAt: Map<String, Long>
    ): Map<Int, ClaudeItemLabelSummary> {
        val priority = listOf("error", "processing", "approval", "startwait", "waiting", "done")
        val childrenByParent = allItems.filter { it.item.mergedParentId != null }
            .groupBy { it.item.mergedParentId!! }
        val result = mutableMapOf<Int, ClaudeItemLabelSummary>()
        for (top in allItems) {
            if (top.item.mergedParentId != null) continue
            // まとめ UUID 集合 (自分+子孫) をメモリ上の親子関係から BFS で解決する (resolveBundleUuids と同じ範囲)
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
            // フェーズ 6-9 是正 (61-2): 件数の母数は親自身の写真のみ。まとめ作成時に子の写真は親へ
            // コピー済み (md5 も継承) で、子側にも残るため、集合全員を走査すると二重に数えてしまう。
            // ジョブ照合用の UUID 集合だけは従来どおり子孫まで含める (PC の claudeItemLabel と同一規則)。
            for (photo in top.photos) {
                val md5 = photo.md5Hash
                if (md5.isBlank()) continue
                val key = claudePhotoLabelKey(top.item.syncUuid, bundleUuids, md5, jobs, seenAt, emptyMap()) ?: continue
                counts[key] = (counts[key] ?: 0) + 1
            }
            val best = priority.firstOrNull { counts.containsKey(it) } ?: continue
            result[top.item.id] = ClaudeItemLabelSummary(best, counts[best] ?: 0)
        }
        return result
    }

    private val _labelBundleUuids = MutableStateFlow<List<String>>(emptyList())
    val labelBundleUuids: StateFlow<List<String>> = _labelBundleUuids.asStateFlow()

    // フェーズ 6-2 (6-2-2): 「いったん閉じる」で非表示にした承認待ちのジョブID集合。
    // この集合に含まれない承認待ちが新たに現れた場合にだけダイアログを再表示する。
    private val _dismissedClaudeApprovalIds = MutableStateFlow<Set<String>>(emptySet())
    val dismissedClaudeApprovalIds: StateFlow<Set<String>> = _dismissedClaudeApprovalIds.asStateFlow()

    // フェーズ 6-2 (6-2-3): 承認待ちジョブのサムネイル (jobId → ローカルパス)。解決できなかったジョブは含まない。
    private val _pendingClaudeApprovalThumbnails = MutableStateFlow<Map<String, String>>(emptyMap())
    val pendingClaudeApprovalThumbnails: StateFlow<Map<String, String>> = _pendingClaudeApprovalThumbnails.asStateFlow()

    // フェーズ 6-2 (6-2-7): 編集画面の帯タップによる表示抑止の解除フラグ。
    private val _claudeApprovalPeekRequested = MutableStateFlow(false)
    val claudeApprovalPeekRequested: StateFlow<Boolean> = _claudeApprovalPeekRequested.asStateFlow()

    private val _unackedClaudeCompletions = MutableStateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>>(emptyList())
    val unackedClaudeCompletions: StateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>> = _unackedClaudeCompletions.asStateFlow()

    // レビュー 24-2: 完了通知を「いったん閉じる」で非表示にしたジョブID集合。
    // 承認側 (6-2-2) と同型で、再取得のたびに未確認リストを作り直しても閉じた完了は復活しない。
    // 掃除は未確認として残っている ID だけに限る (新しい完了の到着で全消去しない)。
    private val _dismissedClaudeCompletionIds = MutableStateFlow<Set<String>>(emptySet())
    val dismissedClaudeCompletionIds: StateFlow<Set<String>> = _dismissedClaudeCompletionIds.asStateFlow()

    // Build 13 (13-4): 承認・確認送信の処理中フラグ (重複タップ遮断) と、
    // 失敗時に MainActivity が一度だけ Toast 表示するワンショットメッセージ。
    private val _claudeActionInProgress = MutableStateFlow(false)
    val claudeActionInProgress: StateFlow<Boolean> = _claudeActionInProgress.asStateFlow()

    private val _claudeActionMessage = MutableStateFlow<String?>(null)
    val claudeActionMessage: StateFlow<String?> = _claudeActionMessage.asStateFlow()

    fun clearClaudeActionMessage() {
        _claudeActionMessage.value = null
    }

    // Build 13 (13-4) / フェーズ 6-2 (6-2-2, 16-3): 「いったん閉じる」。
    // サーバー側の状態は変えず、この端末の表示だけを閉じる。現在の承認待ち全件を
    // 非表示集合へ入れ、抑止解除フラグは false へ戻す (編集画面にいる場合は帯表示に戻る)。
    // 承認待ちはサーバー側では10分で待機へ自動復帰する。完了の未確認は PC 側からも確認できる。
    fun dismissClaudeApprovalForNow() {
        _dismissedClaudeApprovalIds.value = _pendingClaudeApprovals.value.map { it.jobId }.toSet()
        _claudeApprovalPeekRequested.value = false
    }

    // フェーズ 6-2 (6-2-7, 16-3): 編集画面の帯タップ。表示抑止を解除し、
    // 非表示集合もクリアしてダイアログを出す (クリアしないと一度閉じた後に帯を押しても開かない)。
    fun requestClaudeApprovalPeek() {
        _dismissedClaudeApprovalIds.value = emptySet()
        _claudeApprovalPeekRequested.value = true
    }

    fun dismissClaudeCompletionForNow(jobId: String) {
        // レビュー 24-2: リストから消すだけでは次の再取得で復活するため、非表示集合へ記録する
        _dismissedClaudeCompletionIds.value = _dismissedClaudeCompletionIds.value + jobId
    }

    init {
        startHeartbeatLoop()
        startClaudeWatchLoop()
    }

    private fun scheduleSyncSuccessResetToIdle(successTimestamp: Long) {
        syncSuccessResetJob?.cancel()
        syncSuccessResetJob = viewModelScope.launch {
            delay(3500)
            val cur = _syncStatusState.value
            if (cur.phase == SyncPhase.SUCCESS && cur.updatedAt == successTimestamp) {
                _syncStatusState.value = PcSyncProgress(
                    phase = SyncPhase.IDLE,
                    detailMessage = "同期済",
                    updatedAt = System.currentTimeMillis()
                )
            }
        }
    }

    private suspend fun refreshKnownRevisions(url: String, token: String) {
        val hb = pcServerClient.sendHeartbeatWithRevision(url, token)
        if (hb.isConnected) {
            lastKnownDataRevision = hb.dataRevision
            lastKnownSettingsRevision = hb.settingsRevision
            // 既読カウンタに差分があればジョブ状態を取り直す。既知値の更新は
            // refreshClaudeJobStates の成功時にのみ行い、ここでは先行代入しない (§36.2-1)
            if (hb.labelSeenRevision > 0L && hb.labelSeenRevision != lastKnownLabelSeenRevision) {
                refreshClaudeJobStates(url, token)
            }
        }
    }

    // フェーズ 5-4 / 6-2: Claude ジョブ状態の再取得。成功時のみ true を返し、StateFlow を更新する。
    private suspend fun refreshClaudeJobStates(url: String, token: String, fromWatch: Boolean = false): Boolean {
        // レビュー 24-4: 取得と反映を専用 Mutex で直列化し、遅れて返った古い応答が
        // 新しい状態 (承認・確認の反映結果) を上書きしないようにする。
        return claudeRefreshMutex.withLock {
            // 監視経由の取得は、ユーザーの応答処理中は反映しない (20-3 のガードをロック内で担保)
            if (fromWatch && _claudeActionInProgress.value) return@withLock false
            val seqAtStart = markSeenSeq
            val fetched = pcServerClient.fetchClaudeJobs(url, token) ?: return@withLock false
            val jobs = fetched.jobs
            _claudeJobsForLabels.value = jobs
            // 取得中に自端末の既読化が走っていない場合のみ、サーバーの既読状態を基準として採用する。
            // サーバー側カウンタが下がった場合 (接続先変更・保存ファイル再作成) でも取り逃がさず、
            // 取得中に既読化が走った場合はその応答確定値が優先されるため辞書の適用を見送る (37-1 / 34-4-1)。
            if (markSeenSeq == seqAtStart) {
                _claudeLabelSeenAt.value = fetched.labelSeenAtByItemUuid.mapKeys { it.key.lowercase() }
                lastKnownLabelSeenRevision = fetched.labelSeenRevision
            }
            val pending = jobs
                .filter { it.status == "approval_pending" }
                .sortedBy { it.createdAt }
            _pendingClaudeApprovals.value = pending
            // 非表示集合は、まだ承認待ちとして残っている ID だけに掃除する
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

    // フェーズ 6-5 (是正 Build 8): 写真単位の加工ラベル導出 (優先度: エラー > 加工中 > 承認待ち > 待機中 > 加工済み。§27.1.1)
    // 購読済みの State 値を引数で受け取り、呼び出し側の再コンポーズに連動させる (32-4)。
    // snapshotSeenAt が非 null の場合は加工済み判定にその時刻を使う (閲覧セッション中は開いた時点の状態を保つ。32-2 / 34-2)。
    // 戻り値はラベル種別キー ("error" / "processing" / "approval" / "waiting" / "done")、無ければ null。
    fun claudePhotoLabelKey(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobs: List<com.example.myapplication.data.server.ClaudeJobDto>,
        seenAtMap: Map<String, Long>,
        snapshotMap: Map<String, Long>
    ): String? {
        if (itemUuid.isBlank() || md5.isBlank()) return null
        // フェーズ 6-5 是正 (Build 13 / 49-1): まとめ親では子商品のジョブも対象にするため、
        // 単一 UUID ではなくまとめ UUID 集合 (自分＋子孫) で照合する (PC・サーバーと同一範囲)
        // フェーズ 6-10: 導出本体は純粋関数 (ClaudeLabelDerivation) へ移し、§41.5 の単体テストで検証する
        val bundleSet = bundleUuids.ifEmpty { listOf(itemUuid) }
        return com.example.myapplication.data.server.ClaudeLabelDerivation.photoLabelKey(
            bundleSet, md5, jobs, seenAtMap, snapshotMap, System.currentTimeMillis()
        )
    }

    // フェーズ 6-10 (§37.4 / §40.3): チップが示すラベルの源泉ジョブ (取消対象そのもの)
    fun claudePhotoLabelSourceJob(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobs: List<com.example.myapplication.data.server.ClaudeJobDto>
    ): com.example.myapplication.data.server.ClaudeJobDto? {
        if (itemUuid.isBlank() || md5.isBlank()) return null
        val bundleSet = bundleUuids.ifEmpty { listOf(itemUuid) }
        return com.example.myapplication.data.server.ClaudeLabelDerivation.labelSourceJob(bundleSet, md5, jobs)
    }

    // フェーズ 6-10 (§37.4 / §40.3): ラベルチップからのジョブ取消。
    // 送信直前に最新のジョブ状態を取り直し、その写真の源泉ジョブが表示時と変わっていたら
    // 取り消さずに再導出 (表示更新) だけ行う。成功・失敗いずれでもジョブ状態を再取得する。
    fun cancelClaudeJobFromLabel(
        itemUuid: String,
        bundleUuids: List<String>,
        md5: String,
        jobId: String
    ) {
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank() || jobId.isBlank()) return
        if (_claudeActionInProgress.value) return
        _claudeActionInProgress.value = true
        viewModelScope.launch(Dispatchers.IO) {
            try {
                // レビュー 64-2: 再取得は refreshClaudeJobStates の成否で判定し、失敗時は
                // 古いキャッシュで源泉を検証せず、取り消さずに終了する (二重 GET も解消)
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

    // まとめ UUID 集合 (自分＋子孫) をローカル ID の親子関係から BFS で解決する (49-1)
    private suspend fun resolveBundleUuids(itemId: Int, itemUuid: String): List<String> {
        val result = mutableListOf(itemUuid)
        val visitedIds = mutableSetOf(itemId)
        var frontier = listOf(itemId)
        while (frontier.isNotEmpty()) {
            val next = mutableListOf<Int>()
            for (pid in frontier) {
                val children = try { repository.getMergedChildItemsWithPhotos(pid) } catch (e: Exception) { emptyList() }
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

    // フェーズ 6-5 是正 (34-2): 商品の閲覧セッション開始。初回入室時のみスナップショットを確定し、
    // 既読化 API を投げる前の既読時刻を保持する (詳細→編集の遷移では上書きしない)。
    // Build 13 / 49-1: まとめ親では子孫を含む UUID 集合でスナップショットと既読送信を行う。
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
        viewModelScope.launch(Dispatchers.IO) {
            val bundle = resolveBundleUuids(itemId, itemUuid)
            // 別の商品へ移っていた場合は何もしない
            if (!_labelVisitSnapshot.value.containsKey(key)) return@launch
            _labelBundleUuids.value = bundle
            // スナップショットに集合の各 UUID の既読時刻を補う (既にあるキーは保持する)
            val additions = bundle.map { it.lowercase() }
                .filter { !_labelVisitSnapshot.value.containsKey(it) }
                .associateWith { _claudeLabelSeenAt.value[it] ?: 0L }
            if (additions.isNotEmpty()) {
                _labelVisitSnapshot.value = _labelVisitSnapshot.value + additions
            }
            markClaudeLabelSeen(bundle)
        }
    }

    // 閲覧セッション終了 (一覧へ戻ったとき・別商品へ移ったときに呼ぶ)
    fun endLabelVisit() {
        if (_labelVisitSnapshot.value.isNotEmpty()) _labelVisitSnapshot.value = emptyMap()
        if (_labelBundleUuids.value.isNotEmpty()) _labelBundleUuids.value = emptyList()
    }

    // フェーズ 6-5: 商品を開いたら加工済みラベルを既読化する (楽観的にローカルへ反映し、
    // サーバー応答の確定値で上書きする。32-5 / 34-4-1)
    // Build 13 / 49-1: まとめ UUID 集合を一括で既読化する (API は itemUuids 複数対応済み)
    fun markClaudeLabelSeen(itemUuids: List<String>) {
        val keys = itemUuids.filter { it.isNotBlank() }.map { it.lowercase() }.distinct()
        if (keys.isEmpty()) return
        markSeenSeq++
        // 応答が得られない場合のフォールバックは、集合内ジョブの最大完了時刻以上へ丸める (§33.5 / 37-2)
        val fallbackSeenAt = maxOf(
            System.currentTimeMillis(),
            _claudeJobsForLabels.value.filter { it.itemUuid.lowercase() in keys }.maxOfOrNull { it.completedAt } ?: 0L
        )
        _claudeLabelSeenAt.value = _claudeLabelSeenAt.value + keys.associateWith { fallbackSeenAt }
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank()) return
        viewModelScope.launch(Dispatchers.IO) {
            val resp = try { pcServerClient.postClaudeLabelSeen(itemUuids.filter { it.isNotBlank() }.distinct(), url, token) } catch (e: Exception) { null }
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

    // フェーズ 6-2 (6-2-3): 承認待ちジョブのサムネイル解決。
    // (1) 先頭写真の md5 → 写真エンティティ、(2) itemUuid → 商品の先頭写真、
    // (3) いずれも無ければプレースホルダ表示 (マップに含めない)。job.itemId は端末間で一致しないため使わない。
    private suspend fun resolveClaudeApprovalThumbnails(
        pending: List<com.example.myapplication.data.server.ClaudeJobDto>
    ): Map<String, String> {
        val result = mutableMapOf<String, String>()
        for (job in pending) {
            try {
                var path: String? = null
                val md5 = job.photos.firstOrNull()?.md5 ?: ""
                if (md5.isNotBlank()) {
                    val photo = repository.getPhotoByMd5(md5)
                    if (photo != null) {
                        // レビュー 20-2: 端末上に実ファイルが存在するパスのみを採用する
                        // (欠損パスを採用すると itemUuid フォールバックへ進まず、空白表示になる)
                        path = listOfNotNull(photo.thumbnailPath, photo.filePath)
                            .firstOrNull { it.isNotBlank() && java.io.File(it).exists() }
                    }
                }
                if (path == null && job.itemUuid.isNotBlank()) {
                    val withPhotos = repository.getItemWithPhotosBySyncUuid(job.itemUuid)
                    val first = withPhotos?.photos?.minByOrNull { it.displayOrder }
                    if (first != null) {
                        path = listOfNotNull(first.thumbnailPath, first.filePath)
                            .firstOrNull { it.isNotBlank() && java.io.File(it).exists() }
                    }
                }
                if (!path.isNullOrBlank()) {
                    result[job.jobId] = path
                }
            } catch (_: Exception) {
                // サムネイル解決の失敗では承認フロー自体を止めない (プレースホルダ表示になる)
            }
        }
        return result
    }

    // フェーズ 6-2 (6-2-5): 一括承認ダイアログからの応答。
    // 対象は displayedIds (ダイアログに表示した最大10件) に厳密限定する (未表示分には触れない)。
    // チェック済みは古い順に1件ずつ承認し、途中で失敗したらそこで停止して件数を報告する
    // (未処理分は承認待ちのまま残り、待機戻しも行わない)。
    // 表示分のうち未チェック分は待機へ移動する (失敗しても継続し、最後に集計を報告する)。
    fun respondToClaudeApprovals(checkedIds: Set<String>, displayedIds: List<String>) {
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank()) return
        if (_claudeActionInProgress.value) return
        _claudeActionInProgress.value = true
        viewModelScope.launch(Dispatchers.IO) {
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
                // レビュー 24-3: リセットは「失敗なし・移動失敗なし・承認待ち残件なし」の時だけ。
                // 途中失敗や残件がある場合はダイアログを開いたままにして、その場で再試行できるようにする。
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

    // フェーズ 5-5 / Build 13 (13-4): 完了ダイアログからの確認 (ack)。
    // 成功したら未確認リストから外し、最新状態へ整合させる。失敗時も状態を再取得し、
    // サーバー側で確認済みになっていればエラー表示せず閉じる。
    fun acknowledgeClaudeCompletion(jobId: String) {
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank()) return
        if (_claudeActionInProgress.value) return
        _claudeActionInProgress.value = true
        viewModelScope.launch(Dispatchers.IO) {
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

    // レビュー 24-4: Claude ジョブ監視の独立コルーチン。
    // ハートビート本体の値 (hb) を待たず、軽量な変化カウンタ窓口を自分で定期確認する。
    // 本体が写真同期 (syncMutex) で長時間塞がっていても、承認・完了の検知は止まらない。
    // 例外は握りつぶしてログのみとし、本体の同期には一切干渉しない (5-4 の分離原則を維持)。
    private fun startClaudeWatchLoop() {
        claudeWatchJob?.cancel()
        claudeWatchJob = viewModelScope.launch(Dispatchers.IO) {
            while (isActive) {
                val url = _pcServerUrl.value
                val token = _pcAuthToken.value
                if (url.isNotBlank() && !_claudeActionInProgress.value) {
                    try {
                        val revision = pcServerClient.fetchClaudeRevision(url, token)
                        if (revision != null && revision > 0L && revision != lastKnownClaudeRevision) {
                            refreshClaudeJobStates(url, token, fromWatch = true)
                        }
                    } catch (e: Exception) {
                        android.util.Log.e("PC_SYNC", "Claudeジョブ監視に失敗: ${e.message}")
                    }
                }
                delay(4000)
            }
        }
    }

    private fun startHeartbeatLoop() {
        heartbeatJob?.cancel()
        heartbeatJob = viewModelScope.launch(Dispatchers.IO) {
            var wasConnected = false
            while (isActive) {
                val url = _pcServerUrl.value
                val token = _pcAuthToken.value
                if (url.isNotBlank()) {
                    val hb = pcServerClient.sendHeartbeatWithRevision(url, token)
                    val connected = hb.isConnected
                    _isPcServerConnected.value = connected

                    if (!connected) {
                        wasConnected = false
                    } else if (!wasConnected) {
                        // 再接続時ハンドシェイク 未同期ローカル変更を優先PushしてからPC差分を取得
                        wasConnected = true
                        syncMutex.withLock {
                            try {
                                pcServerClient.fetchAndSyncServerSettings(url, token)
                                val hasLocalDirty = repository.getDao().getDirtyItemsCount() > 0 ||
                                    repository.getPendingDeletedUuids().isNotEmpty()
                                var pushSucceeded = true
                                var pushError: String? = null
                                if (hasLocalDirty) {
                                    _syncStatusState.value = PcSyncProgress(
                                        phase = SyncPhase.SYNCING_ITEMS,
                                        progressPercent = 5,
                                        detailMessage = "再接続・差分送信中..."
                                    )
                                    val pushRes = pcServerClient.syncDeltaToPcServer(url, token) { progress ->
                                        _syncStatusState.value = progress
                                    }
                                    pushSucceeded = pushRes.isSuccess
                                    pushError = pushRes.errorMessage
                                }
                                if (pushSucceeded) {
                                    _syncStatusState.value = PcSyncProgress(
                                        phase = SyncPhase.FETCHING_ITEMS,
                                        progressPercent = 92,
                                        detailMessage = "PC側最新データを取得中..."
                                    )
                                    val fetched = pcServerClient.fetchServerItems(url, token)
                                    refreshKnownRevisions(url, token)
                                    if (fetched) {
                                        val now = System.currentTimeMillis()
                                        _lastSyncSuccessAt.value = now
                                        _syncStatusState.value = PcSyncProgress(
                                            phase = SyncPhase.SUCCESS,
                                            progressPercent = 100,
                                            detailMessage = "再接続同期完了",
                                            updatedAt = now
                                        )
                                        scheduleSyncSuccessResetToIdle(now)
                                    } else {
                                        _syncStatusState.value = PcSyncProgress(
                                            phase = SyncPhase.ERROR,
                                            detailMessage = "PCデータ取得に失敗しました",
                                            errorDetail = "GET /api/items の取得に失敗しました"
                                        )
                                    }
                                } else {
                                    _syncStatusState.value = PcSyncProgress(
                                        phase = SyncPhase.ERROR,
                                        detailMessage = "再接続同期に失敗しました",
                                        errorDetail = pushError ?: "PCサーバーとの同期に失敗しました"
                                    )
                                }
                            } catch (e: Exception) {
                                _syncStatusState.value = PcSyncProgress(
                                    phase = SyncPhase.ERROR,
                                    detailMessage = "再接続同期エラー",
                                    errorDetail = e.localizedMessage ?: e.message ?: "再接続時の同期に失敗しました"
                                )
                            }
                        }
                    } else {
                        // 接続維持中 リビジョン番号に変化があったときのみ必要最小限のPullを実行
                        val settingsChanged = hb.settingsRevision > 0L && hb.settingsRevision != lastKnownSettingsRevision
                        val dataChanged = hb.dataRevision > 0L && hb.dataRevision != lastKnownDataRevision
                        if (settingsChanged || dataChanged) {
                            if (syncMutex.tryLock()) {
                                try {
                                    _syncStatusState.value = PcSyncProgress(
                                        phase = SyncPhase.FETCHING_ITEMS,
                                        progressPercent = 80,
                                        detailMessage = "PC側の更新を反映中..."
                                    )
                                    if (settingsChanged) {
                                        pcServerClient.fetchAndSyncServerSettings(url, token)
                                        lastKnownSettingsRevision = hb.settingsRevision
                                    }
                                    var fetchOk = true
                                    if (dataChanged) {
                                        fetchOk = pcServerClient.fetchServerItems(url, token)
                                        if (fetchOk) lastKnownDataRevision = hb.dataRevision
                                    }
                                    if (fetchOk) {
                                        val now = System.currentTimeMillis()
                                        _lastSyncSuccessAt.value = now
                                        _syncStatusState.value = PcSyncProgress(
                                            phase = SyncPhase.SUCCESS,
                                            progressPercent = 100,
                                            detailMessage = "PC更新を反映しました",
                                            updatedAt = now
                                        )
                                        scheduleSyncSuccessResetToIdle(now)
                                    } else {
                                        _syncStatusState.value = PcSyncProgress(
                                            phase = SyncPhase.ERROR,
                                            detailMessage = "PC更新の取得に失敗",
                                            errorDetail = "PCサーバーからの差分取得に失敗しました"
                                        )
                                    }
                                } catch (e: Exception) {
                                    _syncStatusState.value = PcSyncProgress(
                                        phase = SyncPhase.ERROR,
                                        detailMessage = "PC更新反映エラー",
                                        errorDetail = e.localizedMessage ?: e.message ?: "差分取得エラー"
                                    )
                                } finally {
                                    syncMutex.unlock()
                                }
                            }
                        }
                    }
                    // フェーズ 6-5 是正 (34-4-2): 既読カウンタの変化検知は syncMutex の外で
                    // ジョブ状態を取り直す (写真同期にブロックされない経路)
                    if (connected && hb.labelSeenRevision > 0L && hb.labelSeenRevision != lastKnownLabelSeenRevision) {
                        refreshClaudeJobStates(url, token, fromWatch = true)
                    }
                    // Claude ジョブの検知は独立した監視コルーチンへ移設した (レビュー 24-4)。
                    // 写真同期でこのループが塞がっていても検知が止まらないようにするため。
                } else {
                    _isPcServerConnected.value = false
                    wasConnected = false
                }
                delay(4000)
            }
        }
    }

    /**
     * ローカルデータ変更時にバックグラウンドでPC母艦へ自動差分Push（500msデバウンス・Mutex排他）
     */
    fun triggerAutoSync() {
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank() || !_isPcServerConnected.value) return

        autoSyncJob?.cancel()
        autoSyncJob = viewModelScope.launch(Dispatchers.IO) {
            delay(500)
            syncMutex.withLock {
                try {
                    _syncStatusState.value = PcSyncProgress(
                        phase = SyncPhase.SYNCING_ITEMS,
                        progressPercent = 5,
                        detailMessage = "自動同期を開始..."
                    )
                    pcServerClient.fetchAndSyncServerSettings(url, token)
                    val result = pcServerClient.syncDeltaToPcServer(url, token) { progress ->
                        _syncStatusState.value = progress
                    }
                    if (result.isSuccess) {
                        _syncStatusState.value = PcSyncProgress(
                            phase = SyncPhase.FETCHING_ITEMS,
                            progressPercent = 92,
                            detailMessage = "PC側最新データを取得中..."
                        )
                        pcServerClient.fetchServerItems(url, token)
                        refreshKnownRevisions(url, token)
                        val now = System.currentTimeMillis()
                        _lastSyncSuccessAt.value = now
                        _syncStatusState.value = PcSyncProgress(
                            phase = SyncPhase.SUCCESS,
                            progressPercent = 100,
                            detailMessage = "同期完了 (${result.syncedItemsCount}件)",
                            updatedAt = now
                        )
                        scheduleSyncSuccessResetToIdle(now)
                    } else {
                        _syncStatusState.value = PcSyncProgress(
                            phase = SyncPhase.ERROR,
                            detailMessage = "自動同期に失敗しました",
                            errorDetail = result.errorMessage ?: "PCサーバーとの同期に失敗しました"
                        )
                    }
                } catch (e: Exception) {
                    _syncStatusState.value = PcSyncProgress(
                        phase = SyncPhase.ERROR,
                        detailMessage = "自動同期エラー",
                        errorDetail = e.localizedMessage ?: e.message ?: "通信エラーが発生しました"
                    )
                }
            }
        }
    }

    fun updatePcServerUrl(url: String) {
        val changed = _pcServerUrl.value != url
        _pcServerUrl.value = url
        pcServerClient.saveServerUrl(url)
        if (changed) {
            // レビュー 24-8: 接続先を切り替えたら、前サーバーのカウンタ・通知状態を引き継がない
            lastKnownDataRevision = -1L
            lastKnownSettingsRevision = -1L
            lastKnownClaudeRevision = -1L
            _pendingClaudeApprovals.value = emptyList()
            _pendingClaudeApprovalThumbnails.value = emptyMap()
            _dismissedClaudeApprovalIds.value = emptySet()
            _claudeApprovalPeekRequested.value = false
            _unackedClaudeCompletions.value = emptyList()
            _dismissedClaudeCompletionIds.value = emptySet()
            // フェーズ 6-5 是正 (37-1): 加工ラベルの状態も前サーバーのものを引き継がない
            lastKnownLabelSeenRevision = 0L
            _claudeJobsForLabels.value = emptyList()
            _claudeLabelSeenAt.value = emptyMap()
            _labelVisitSnapshot.value = emptyMap()
            _labelBundleUuids.value = emptyList()
        }
    }

    fun updatePcAuthToken(token: String) {
        _pcAuthToken.value = token
        pcServerClient.saveAuthToken(token)
    }

    fun parseQrCodeText(qrText: String): Boolean {
        val text = qrText.trim()
        if (!text.startsWith("furima:")) return false

        val content = text.removePrefix("furima:")
        val parts = content.split("|")
        if (parts.size >= 2) {
            val url = parts[0].trim()
            val token = parts[1].trim()
            val storeId = if (parts.size >= 3) parts[2].trim() else ""

            if (url.isNotBlank()) {
                updatePcServerUrl(url)
                updatePcAuthToken(token)
                if (storeId.isNotBlank()) {
                    pcServerClient.saveStoreId(storeId)
                }
                return true
            }
        }
        return false
    }

    fun testPcServerConnection(onResult: (Boolean) -> Unit) {
        viewModelScope.launch {
            val url = _pcServerUrl.value
            val token = _pcAuthToken.value
            val connected = pcServerClient.checkServerStatus(url, token)
            _isPcServerConnected.value = connected
            if (connected) {
                refreshKnownRevisions(url, token)
            }
            onResult(connected)
        }
    }

    fun syncDeltaToPcServer(onResult: (com.example.myapplication.data.server.PcSyncResult) -> Unit) {
        viewModelScope.launch {
            val url = _pcServerUrl.value
            val token = _pcAuthToken.value
            val result = syncMutex.withLock {
                try {
                    _syncStatusState.value = PcSyncProgress(
                        phase = SyncPhase.SYNCING_ITEMS,
                        progressPercent = 5,
                        detailMessage = "同期を開始中..."
                    )
                    pcServerClient.fetchAndSyncServerSettings(url, token)
                    val pushRes = pcServerClient.syncDeltaToPcServer(url, token) { progress ->
                        _syncStatusState.value = progress
                    }
                    if (pushRes.isSuccess) {
                        _syncStatusState.value = PcSyncProgress(
                            phase = SyncPhase.FETCHING_ITEMS,
                            progressPercent = 92,
                            detailMessage = "PC側最新データを取得中..."
                        )
                        pcServerClient.fetchServerItems(url, token)
                        refreshKnownRevisions(url, token)
                        val now = System.currentTimeMillis()
                        _lastSyncSuccessAt.value = now
                        _syncStatusState.value = PcSyncProgress(
                            phase = SyncPhase.SUCCESS,
                            progressPercent = 100,
                            detailMessage = "同期完了 (${pushRes.syncedItemsCount}件)",
                            updatedAt = now
                        )
                        scheduleSyncSuccessResetToIdle(now)
                    } else {
                        _syncStatusState.value = PcSyncProgress(
                            phase = SyncPhase.ERROR,
                            detailMessage = "同期に失敗しました",
                            errorDetail = pushRes.errorMessage ?: "PCサーバーとの同期に失敗しました"
                        )
                    }
                    pushRes
                } catch (e: Exception) {
                    val errMsg = e.localizedMessage ?: e.message ?: "同期中に通信エラーが発生しました"
                    _syncStatusState.value = PcSyncProgress(
                        phase = SyncPhase.ERROR,
                        detailMessage = "同期エラー",
                        errorDetail = errMsg
                    )
                    com.example.myapplication.data.server.PcSyncResult(
                        isSuccess = false,
                        errorMessage = errMsg
                    )
                }
            }
            _isPcServerConnected.value = result.isSuccess
            onResult(result)
        }
    }

    override fun onCleared() {
        super.onCleared()
        heartbeatJob?.cancel()
        webServer.stop()
    }
}
