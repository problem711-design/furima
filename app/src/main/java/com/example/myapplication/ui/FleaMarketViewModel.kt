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
                "DRAFT" -> item.status == "DRAFT"
                else -> true
            }

            matchesQuery && matchesStatus
        }.sortedWith { a, b ->
            when (sort) {
                SortOption.DATE_DESC -> b.item.createdAt.compareTo(a.item.createdAt)
                SortOption.DATE_ASC -> a.item.createdAt.compareTo(b.item.createdAt)
                SortOption.SOLD_DATE_DESC -> (b.item.soldAt ?: 0L).compareTo(a.item.soldAt ?: 0L)
                SortOption.PRICE_DESC -> {
                    val pB = if (b.item.status == "SOLD" && b.item.soldPrice > 0) b.item.soldPrice else b.item.price
                    val pA = if (a.item.status == "SOLD" && a.item.soldPrice > 0) a.item.soldPrice else a.item.price
                    pB.compareTo(pA)
                }
                SortOption.PRICE_ASC -> {
                    val pB = if (b.item.status == "SOLD" && b.item.soldPrice > 0) b.item.soldPrice else b.item.price
                    val pA = if (a.item.status == "SOLD" && a.item.soldPrice > 0) a.item.soldPrice else a.item.price
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
            if (item.status == "SOLD") {
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

    // 「Museに送る」送信ラッパー (フェーズ 5-3)。photos は (md5Hash, fileName) のリスト。
    fun sendPhotosToClaude(
        itemId: Int,
        itemUuid: String,
        photos: List<Pair<String, String>>,
        templateId: String,
        queue: String = "patrol",
        onError: (String) -> Unit = {},
        onSuccess: () -> Unit
    ) {
        viewModelScope.launch {
            if (!_isPcServerConnected.value) {
                onError("PCサーバー未接続です")
                return@launch
            }
            val result = pcServerClient.createClaudeJob(itemId, itemUuid, photos, templateId, queue)
            if (result.success) {
                onSuccess()
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
    private var autoSyncJob: Job? = null
    private var syncSuccessResetJob: Job? = null
    private val syncMutex = Mutex()
    @Volatile private var lastKnownDataRevision: Long = -1L
    @Volatile private var lastKnownSettingsRevision: Long = -1L
    @Volatile private var lastKnownClaudeRevision: Long = -1L

    // フェーズ 5-4: 承認待ちジョブと未確認の完了ジョブ (ハートビート第3分岐で更新)
    private val _pendingClaudeApproval = MutableStateFlow<com.example.myapplication.data.server.ClaudeJobDto?>(null)
    val pendingClaudeApproval: StateFlow<com.example.myapplication.data.server.ClaudeJobDto?> = _pendingClaudeApproval.asStateFlow()

    private val _unackedClaudeCompletions = MutableStateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>>(emptyList())
    val unackedClaudeCompletions: StateFlow<List<com.example.myapplication.data.server.ClaudeJobDto>> = _unackedClaudeCompletions.asStateFlow()

    // Build 13 (13-4): 承認・確認送信の処理中フラグ (重複タップ遮断) と、
    // 失敗時に MainActivity が一度だけ Toast 表示するワンショットメッセージ。
    private val _claudeActionInProgress = MutableStateFlow(false)
    val claudeActionInProgress: StateFlow<Boolean> = _claudeActionInProgress.asStateFlow()

    private val _claudeActionMessage = MutableStateFlow<String?>(null)
    val claudeActionMessage: StateFlow<String?> = _claudeActionMessage.asStateFlow()

    fun clearClaudeActionMessage() {
        _claudeActionMessage.value = null
    }

    // Build 13 (13-4): 「いったん閉じる」。サーバー側の状態は変えず、この端末の表示だけを閉じる。
    // 承認待ちは次回の状態再取得 (リビジョン変化時・アプリ再起動時) で再表示され、
    // サーバー側では10分で queued へ自動復帰する。完了の未確認は PC 側からも確認できる。
    fun dismissClaudeApprovalForNow() {
        _pendingClaudeApproval.value = null
    }

    fun dismissClaudeCompletionForNow(jobId: String) {
        _unackedClaudeCompletions.value = _unackedClaudeCompletions.value.filter { it.jobId != jobId }
    }

    init {
        startHeartbeatLoop()
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
        }
    }

    // フェーズ 5-4: Claude ジョブ状態の再取得。成功時のみ true を返し、StateFlow を更新する。
    private suspend fun refreshClaudeJobStates(url: String, token: String): Boolean {
        val jobs = pcServerClient.fetchClaudeJobs(url, token) ?: return false
        _pendingClaudeApproval.value = jobs
            .filter { it.status == "approval_pending" }
            .minByOrNull { it.createdAt }
        _unackedClaudeCompletions.value = jobs.filter {
            (it.status == "done" || it.status == "done_with_errors") && !it.acknowledged
        }
        return true
    }

    // フェーズ 5-4 / Build 13 (13-4): 承認ダイアログからの応答。
    // 成功したら対象をクリアし、最新状態へ整合させる。失敗時も状態を再取得し、
    // サーバー側で既に遷移済み (二度押し等) ならエラー表示せず正常終了として閉じる。
    // 対象がまだ承認待ちのままなら、理由をワンショットメッセージで知らせる。
    fun respondToClaudeApproval(jobId: String, approved: Boolean) {
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank()) return
        if (_claudeActionInProgress.value) return
        _claudeActionInProgress.value = true
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val ok = pcServerClient.postClaudeApproval(jobId, approved, url, token)
                if (ok) {
                    if (_pendingClaudeApproval.value?.jobId == jobId) {
                        _pendingClaudeApproval.value = null
                    }
                    try {
                        refreshClaudeJobStates(url, token)
                    } catch (_: Exception) {
                    }
                } else {
                    try {
                        refreshClaudeJobStates(url, token)
                    } catch (_: Exception) {
                    }
                    if (_pendingClaudeApproval.value?.jobId == jobId) {
                        _claudeActionMessage.value = "PCサーバーへ応答を送信できませんでした。接続を確認してもう一度お試しください。"
                    }
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
                    // 第3分岐 (フェーズ 5-4): Claude ジョブの承認待ち・未確認完了の検知。
                    // 既存のデータPull/設定Pull分岐と isDirty 保護には一切触れない。
                    // この分岐の例外は握りつぶしてログのみとし、同期本体を止めない。
                    if (connected) {
                        try {
                            if (hb.claudeRevision > 0L && hb.claudeRevision != lastKnownClaudeRevision) {
                                if (refreshClaudeJobStates(url, token)) {
                                    lastKnownClaudeRevision = hb.claudeRevision
                                }
                            }
                        } catch (e: Exception) {
                            android.util.Log.e("PC_SYNC", "Claudeジョブ状態の取得に失敗: ${e.message}")
                        }
                    }
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
        _pcServerUrl.value = url
        pcServerClient.saveServerUrl(url)
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
