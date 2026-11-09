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
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.SalesSummary
import com.example.myapplication.data.server.LocalWebServer
import com.example.myapplication.data.server.NetworkUtils
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
        sb.append("【原本なし写真一覧】\n")
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
            current.add(id)
        }
        _selectedItemIds.value = current
    }

    fun clearSelection() {
        _selectedItemIds.value = emptySet()
        _isSelectionMode.value = false
    }

    fun updateDefaultSettings(newSettings: DefaultSettings) {
        repository.updateDefaultSettings(newSettings)
    }

    suspend fun getItemById(id: Int): ItemWithPhotos? {
        return repository.getItemById(id)
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
        onSuccess: () -> Unit
    ) {
        viewModelScope.launch {
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
            onSuccess()
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
            onSuccess(newId)
        }
    }

    /**
     * まとめ買い出品を解除（バラす）して元の子商品を出品中に復元
     */
    fun unmergeItem(parentId: Int, onSuccess: () -> Unit = {}) {
        viewModelScope.launch {
            repository.unmergeItem(parentId)
            onSuccess()
        }
    }

    suspend fun getChildItems(parentId: Int): List<ItemWithPhotos> {
        return repository.getMergedChildItemsWithPhotos(parentId)
    }

    fun updateItemStatus(item: ItemEntity, newStatus: String) {
        viewModelScope.launch {
            repository.updateItemStatus(item, newStatus)
        }
    }

    fun deleteItem(itemWithPhotos: ItemWithPhotos, onSuccess: () -> Unit) {
        viewModelScope.launch {
            repository.deleteItem(itemWithPhotos)
            onSuccess()
        }
    }

    /**
     * PC送信：無圧縮ZIP（商品名.zip）に写真と商品説明文.txtを同梱して共有し、isSharedフラグを更新
     */
    fun shareItemWithZip(context: Context, itemWithPhotos: ItemWithPhotos) {
        viewModelScope.launch {
            val item = itemWithPhotos.item
            val photos = itemWithPhotos.photos.map { it.filePath }
            val shareIntent = withContext(Dispatchers.IO) {
                repository.getPhotoStorageManager().createStoredZipForSharing(
                    itemTitle = item.title,
                    description = item.description,
                    photoPaths = photos
                )
            }
            if (shareIntent != null) {
                repository.markItemShared(item.id)
                val chooser = Intent.createChooser(shareIntent, "PCに送信 (LocalSend等で共有)")
                chooser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                context.startActivity(chooser)
            } else {
                Toast.makeText(context, "ZIPファイルの生成に失敗しました", Toast.LENGTH_SHORT).show()
            }
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
                for (itemWithPhotos in items) {
                    val count = repository.getPhotoStorageManager().deleteOriginalPhotosKeepThumbnails(itemWithPhotos.photos)
                    totalDeleted += count
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
        pcServerClient.getSavedServerUrl().ifBlank { "http://10.0.2.2:8082" }
    )
    val pcServerUrl: StateFlow<String> = _pcServerUrl.asStateFlow()

    private val _pcAuthToken = MutableStateFlow<String>(
        pcServerClient.getSavedAuthToken()
    )
    val pcAuthToken: StateFlow<String> = _pcAuthToken.asStateFlow()

    private val _isPcServerConnected = MutableStateFlow<Boolean>(false)
    val isPcServerConnected: StateFlow<Boolean> = _isPcServerConnected.asStateFlow()

    private var heartbeatJob: Job? = null

    init {
        startHeartbeatLoop()
    }

    private fun startHeartbeatLoop() {
        heartbeatJob?.cancel()
        heartbeatJob = viewModelScope.launch(Dispatchers.IO) {
            while (isActive) {
                val url = _pcServerUrl.value
                val token = _pcAuthToken.value
                if (url.isNotBlank()) {
                    val connected = pcServerClient.sendHeartbeat(url, token)
                    _isPcServerConnected.value = connected
                } else {
                    _isPcServerConnected.value = false
                }
                delay(4000)
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
            val connected = pcServerClient.checkServerStatus(_pcServerUrl.value, _pcAuthToken.value)
            _isPcServerConnected.value = connected
            if (connected) {
                pcServerClient.sendHeartbeat(_pcServerUrl.value, _pcAuthToken.value)
            }
            onResult(connected)
        }
    }

    fun syncDeltaToPcServer(onResult: (com.example.myapplication.data.server.PcSyncResult) -> Unit) {
        viewModelScope.launch {
            val result = pcServerClient.syncDeltaToPcServer(_pcServerUrl.value, _pcAuthToken.value)
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
