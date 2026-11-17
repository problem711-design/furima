package com.example.myapplication.data

import android.content.Context
import android.net.Uri
import com.example.myapplication.data.local.AppDatabase
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemDao
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.isSoldFamilyStatus
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.local.SalesSummary
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.withContext
import java.io.File

enum class BatchPriceMode {
    NONE,
    FIXED,
    DELTA_YEN,
    DELTA_PERCENT
}

data class BatchEditSpec(
    val newStatus: String? = null,
    val applyCostPreset: Boolean = false,
    val useCustomSettings: Boolean = false,
    val feeRatePercent: Int = 5,
    val shippingCost: Int = 210,
    val expenseCost: Int = 55,
    val priceMode: BatchPriceMode = BatchPriceMode.NONE,
    val priceValue: Int = 0
)

class FleaMarketRepository(private val context: Context) {
    private val db = AppDatabase.getDatabase(context)
    private val dao = db.itemDao()
    private val photoStorageManager = PhotoStorageManager(context)
    private val defaultSettingsManager = DefaultSettingsManager(context)

    val defaultSettings: StateFlow<DefaultSettings> = defaultSettingsManager.settings

    private val syncTombstonePrefs =
        context.getSharedPreferences("flea_market_sync_tombstones", Context.MODE_PRIVATE)

    fun addPendingDeletedUuid(uuid: String) {
        val clean = uuid.trim()
        if (clean.isBlank()) return
        synchronized(syncTombstonePrefs) {
            val current = (syncTombstonePrefs.getStringSet("pending_deleted_uuids", emptySet()) ?: emptySet()).toMutableSet()
            current.add(clean)
            syncTombstonePrefs.edit().putStringSet("pending_deleted_uuids", current).apply()
        }
    }

    fun getPendingDeletedUuids(): Set<String> {
        synchronized(syncTombstonePrefs) {
            return (syncTombstonePrefs.getStringSet("pending_deleted_uuids", emptySet()) ?: emptySet()).toSet()
        }
    }

    fun removePendingDeletedUuids(uuids: Collection<String>) {
        if (uuids.isEmpty()) return
        synchronized(syncTombstonePrefs) {
            val current = (syncTombstonePrefs.getStringSet("pending_deleted_uuids", emptySet()) ?: emptySet()).toMutableSet()
            current.removeAll(uuids.map { it.trim() }.toSet())
            syncTombstonePrefs.edit().putStringSet("pending_deleted_uuids", current).apply()
        }
    }

    // レビュー 24-7: 未Pushの写真削除の記録。商品キーはローカル数値IDではなく syncUuid を使う。
    // isDirty 中の加算マージ (PcServerClient) で、削除したはずの写真がサーバーから
    // 復活しないよう、送信が成功するまで (syncUuid, md5Hash) を保持して除外に使う。
    fun recordPendingPhotoDeletion(syncUuid: String, md5Hash: String) {
        val uuid = syncUuid.trim()
        val md5 = md5Hash.trim().lowercase()
        if (uuid.isBlank() || md5.isBlank()) return
        synchronized(syncTombstonePrefs) {
            val key = "pending_deleted_photo_hashes_$uuid"
            val current = (syncTombstonePrefs.getStringSet(key, emptySet()) ?: emptySet()).toMutableSet()
            current.add(md5)
            syncTombstonePrefs.edit().putStringSet(key, current).apply()
        }
    }

    fun getPendingPhotoDeletions(syncUuid: String): Set<String> {
        val uuid = syncUuid.trim()
        if (uuid.isBlank()) return emptySet()
        synchronized(syncTombstonePrefs) {
            return (syncTombstonePrefs.getStringSet("pending_deleted_photo_hashes_$uuid", emptySet()) ?: emptySet()).toSet()
        }
    }

    fun getAllPendingPhotoDeletions(): Map<String, Set<String>> {
        synchronized(syncTombstonePrefs) {
            val result = mutableMapOf<String, Set<String>>()
            for ((key, value) in syncTombstonePrefs.all) {
                if (key.startsWith("pending_deleted_photo_hashes_") && value is Set<*>) {
                    @Suppress("UNCHECKED_CAST")
                    result[key.removePrefix("pending_deleted_photo_hashes_")] = (value as Set<String>).toSet()
                }
            }
            return result
        }
    }

    // 送信に含めた記録だけを消去する (送信中に新たに記録された分は残す)
    fun removePendingPhotoDeletions(recorded: Map<String, Set<String>>) {
        if (recorded.isEmpty()) return
        synchronized(syncTombstonePrefs) {
            val editor = syncTombstonePrefs.edit()
            for ((uuid, hashes) in recorded) {
                val key = "pending_deleted_photo_hashes_$uuid"
                val current = (syncTombstonePrefs.getStringSet(key, emptySet()) ?: emptySet()).toMutableSet()
                current.removeAll(hashes)
                if (current.isEmpty()) editor.remove(key) else editor.putStringSet(key, current)
            }
            editor.apply()
        }
    }

    // 削除した写真と同一 MD5 の写真をその場で再追加した場合は、その記録を解除する
    fun releasePendingPhotoDeletion(syncUuid: String, md5Hash: String) {
        val uuid = syncUuid.trim()
        val md5 = md5Hash.trim().lowercase()
        if (uuid.isBlank() || md5.isBlank()) return
        synchronized(syncTombstonePrefs) {
            val key = "pending_deleted_photo_hashes_$uuid"
            val current = (syncTombstonePrefs.getStringSet(key, emptySet()) ?: emptySet()).toMutableSet()
            if (current.remove(md5)) {
                if (current.isEmpty()) {
                    syncTombstonePrefs.edit().remove(key).apply()
                } else {
                    syncTombstonePrefs.edit().putStringSet(key, current).apply()
                }
            }
        }
    }

    fun updateDefaultSettings(newSettings: DefaultSettings, stampNow: Boolean = true) {
        defaultSettingsManager.updateSettings(newSettings, stampNow)
    }

    fun getAllActiveItemsWithPhotos(): Flow<List<ItemWithPhotos>> = dao.getAllActiveItemsWithPhotos()
    fun getAllItemsWithPhotos(): Flow<List<ItemWithPhotos>> = dao.getAllItemsWithPhotos()

    suspend fun getActiveItemsWithPhotosDirect(): List<ItemWithPhotos> = dao.getActiveItemsWithPhotosDirect()

    // フェーズ 6-2: 一括承認ダイアログのサムネイル解決用
    suspend fun getPhotoByMd5(md5: String): PhotoEntity? = dao.getPhotoByMd5(md5)

    suspend fun getItemWithPhotosBySyncUuid(syncUuid: String): ItemWithPhotos? = dao.getItemWithPhotosBySyncUuid(syncUuid)

    fun getTrashedItemsWithPhotos(): Flow<List<ItemWithPhotos>> = dao.getTrashedItemsWithPhotos()

    fun getTrashedCount(): Flow<Int> = dao.getTrashedCount()

    fun getAllItems(): Flow<List<ItemEntity>> = dao.getAllActiveItems()

    fun getSalesSummary(): Flow<SalesSummary> {
        return combine(dao.getAllActiveItems(), defaultSettings) { items, settings ->
            var totalSales = 0
            var totalProfit = 0
            var listedCount = 0
            var soldCount = 0

            for (item in items) {
                if (isSoldFamilyStatus(item.status)) {
                    soldCount++
                    val price = if (item.soldPrice > 0) item.soldPrice else item.price
                    totalSales += price
                    totalProfit += item.getCalculatedProfit(settings)
                } else if (item.status == "LISTED") {
                    listedCount++
                }
            }
            SalesSummary(totalSales, totalProfit, listedCount, soldCount)
        }
    }

    suspend fun getItemById(id: Int): ItemWithPhotos? {
        return dao.getItemWithPhotosById(id)
    }

    suspend fun saveItem(
        id: Int = 0,
        title: String,
        description: String = "",
        status: String,
        price: Int,
        soldPrice: Int,
        shippingCost: Int,
        feeRatePercent: Int,
        expenseCost: Int,
        useCustomSettings: Boolean,
        imageUris: List<Uri>,
        existingFilePaths: List<String> = emptyList()
    ): Long = withContext(Dispatchers.IO) {
        val existingItem = if (id != 0) dao.getItemWithPhotosById(id)?.item else null
        val now = System.currentTimeMillis()
        val soldAt = if (isSoldFamilyStatus(status)) {
            existingItem?.soldAt ?: now
        } else {
            null
        }
        val preservedUuid = existingItem?.syncUuid?.takeIf { it.isNotBlank() }
            ?: java.util.UUID.randomUUID().toString()

        val item = ItemEntity(
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
            isShared = existingItem?.isShared ?: false,
            mergedParentId = existingItem?.mergedParentId,
            mergedChildCount = existingItem?.mergedChildCount ?: 0,
            mergedChildTitles = existingItem?.mergedChildTitles ?: "",
            originalTotalBundlePrice = existingItem?.originalTotalBundlePrice ?: 0,
            discountAmount = existingItem?.discountAmount ?: 0,
            bundleLevel = existingItem?.bundleLevel ?: 0,
            createdAt = existingItem?.createdAt ?: now,
            updatedAt = now,
            soldAt = soldAt,
            syncUuid = preservedUuid,
            deletedAt = existingItem?.deletedAt,
            isDirty = true
        )

        val itemId = if (id == 0) {
            dao.insertItem(item).toInt()
        } else {
            dao.updateItem(item)
            id
        }

        val currentSettings = defaultSettings.value
        val existingPhotos = if (id != 0) dao.getPhotosByItemId(itemId) else emptyList()
        val existingByFilePath = existingPhotos.filter { it.filePath.isNotBlank() }.associateBy { it.filePath }
        val existingByThumbPath = existingPhotos.filter { !it.thumbnailPath.isNullOrBlank() }.associateBy { it.thumbnailPath!! }

        val newPhotoEntities = mutableListOf<PhotoEntity>()
        var orderIndex = 0

        // 既存写真の保持
        for (path in existingFilePaths) {
            val existing = existingByFilePath[path] ?: existingByThumbPath[path]
            val md5 = if (existing != null && existing.md5Hash.isNotBlank()) {
                existing.md5Hash
            } else {
                photoStorageManager.calculateMd5(File(path))
            }
            if (existing != null) {
                newPhotoEntities.add(existing.copy(displayOrder = orderIndex++, md5Hash = md5, updatedAt = System.currentTimeMillis()))
            } else {
                val thumb = photoStorageManager.createThumbnail(
                    File(path),
                    currentSettings.thumbnailMaxDimension,
                    currentSettings.thumbnailQuality
                )
                newPhotoEntities.add(
                    PhotoEntity(
                        itemId = itemId,
                        filePath = path,
                        thumbnailPath = thumb,
                        md5Hash = md5,
                        displayOrder = orderIndex++
                    )
                )
            }
        }

        // 新規写真の保存
        for (uri in imageUris) {
            val savedResult = photoStorageManager.savePhotoFromUri(
                title,
                uri,
                thumbQuality = currentSettings.thumbnailQuality,
                thumbMaxDim = currentSettings.thumbnailMaxDimension
            )
            if (savedResult != null) {
                val md5 = photoStorageManager.calculateMd5(File(savedResult.first))
                newPhotoEntities.add(
                    PhotoEntity(
                        itemId = itemId,
                        filePath = savedResult.first,
                        thumbnailPath = savedResult.second,
                        md5Hash = md5,
                        displayOrder = orderIndex++
                    )
                )
            }
        }

        if (id != 0 && existingPhotos.isNotEmpty()) {
            val keptPaths = newPhotoEntities.flatMap {
                listOfNotNull(
                    it.filePath.takeIf { p -> p.isNotBlank() },
                    it.thumbnailPath?.takeIf { p -> p.isNotBlank() }
                )
            }.toSet()
            val removedPhotos = existingPhotos.filter { old ->
                (old.filePath.isNotBlank() && !keptPaths.contains(old.filePath)) ||
                    (!old.thumbnailPath.isNullOrBlank() && !keptPaths.contains(old.thumbnailPath))
            }
            if (removedPhotos.isNotEmpty()) {
                // レビュー 24-7: 編集保存で外れた写真も未Push削除として記録する (deletePhotosByIds と同経路の扱い)
                for (removed in removedPhotos) {
                    if (removed.md5Hash.isNotBlank()) {
                        recordPendingPhotoDeletion(preservedUuid, removed.md5Hash)
                    }
                }
                val otherReferencedPaths = dao.getPhotosExcludingItem(itemId).flatMap {
                    listOfNotNull(
                        it.filePath.takeIf { p -> p.isNotBlank() },
                        it.thumbnailPath?.takeIf { p -> p.isNotBlank() }
                    )
                }.toSet()
                for (removed in removedPhotos) {
                    if (removed.filePath.isNotBlank() &&
                        !keptPaths.contains(removed.filePath) &&
                        !otherReferencedPaths.contains(removed.filePath)
                    ) {
                        try {
                            File(removed.filePath).delete()
                        } catch (_: Exception) {
                        }
                    }
                    if (!removed.thumbnailPath.isNullOrBlank() &&
                        !keptPaths.contains(removed.thumbnailPath) &&
                        !otherReferencedPaths.contains(removed.thumbnailPath)
                    ) {
                        try {
                            File(removed.thumbnailPath).delete()
                        } catch (_: Exception) {
                        }
                    }
                }
            }
        }

        dao.deletePhotosByItemId(itemId)
        dao.insertPhotos(newPhotoEntities)

        // レビュー 24-7 補正: 保存後の写真一覧に同一 MD5 があれば、未Push削除の記録を解除する
        for (savedPhoto in newPhotoEntities) {
            if (savedPhoto.md5Hash.isNotBlank()) {
                releasePendingPhotoDeletion(preservedUuid, savedPhoto.md5Hash)
            }
        }

        return@withContext itemId.toLong()
    }

    suspend fun removePhotoFromItem(itemId: Int, photoId: Int): Boolean {
        val itemWithPhotos = dao.getItemWithPhotosById(itemId) ?: return false
        val targetPhoto = itemWithPhotos.photos.firstOrNull { it.id == photoId } ?: return false
        val remaining = itemWithPhotos.photos.filter { it.id != photoId }

        val keptPaths = remaining.flatMap {
            listOfNotNull(
                it.filePath.takeIf { p -> p.isNotBlank() },
                it.thumbnailPath?.takeIf { p -> p.isNotBlank() }
            )
        }.toSet()
        val otherReferencedPaths = dao.getPhotosExcludingItem(itemId).flatMap {
            listOfNotNull(
                it.filePath.takeIf { p -> p.isNotBlank() },
                it.thumbnailPath?.takeIf { p -> p.isNotBlank() }
            )
        }.toSet()

        if (targetPhoto.filePath.isNotBlank() &&
            !keptPaths.contains(targetPhoto.filePath) &&
            !otherReferencedPaths.contains(targetPhoto.filePath)
        ) {
            try {
                File(targetPhoto.filePath).delete()
            } catch (_: Exception) {
            }
        }
        if (!targetPhoto.thumbnailPath.isNullOrBlank() &&
            !keptPaths.contains(targetPhoto.thumbnailPath) &&
            !otherReferencedPaths.contains(targetPhoto.thumbnailPath)
        ) {
            try {
                File(targetPhoto.thumbnailPath).delete()
            } catch (_: Exception) {
            }
        }

        // レビュー 25-1: 詳細画面からの単体削除でも未Push削除を記録する (他の削除経路と同一)
        if (targetPhoto.md5Hash.isNotBlank()) {
            recordPendingPhotoDeletion(itemWithPhotos.item.syncUuid, targetPhoto.md5Hash)
        }
        val now = System.currentTimeMillis()
        val reindexed = remaining.mapIndexed { idx, photo ->
            photo.copy(displayOrder = idx, updatedAt = now)
        }
        dao.deletePhotosByItemId(itemId)
        if (reindexed.isNotEmpty()) {
            dao.insertPhotos(reindexed)
        }
        dao.updateItem(itemWithPhotos.item.copy(updatedAt = now, isDirty = true))
        return true
    }

    suspend fun updateItemStatus(item: ItemEntity, newStatus: String) {
        val now = System.currentTimeMillis()
        // レビュー §30.4-3: 売却系の出入りを他経路 (保存・一括編集) と揃える。
        // 売却系から外れたら売却価格・日時を消し、入った場合は未設定の売却価格を出品価格で補う。
        val inFamily = isSoldFamilyStatus(newStatus)
        val soldAt = if (inFamily) (item.soldAt ?: now) else null
        val soldPrice = when {
            !inFamily -> 0
            item.soldPrice > 0 -> item.soldPrice
            else -> item.price
        }
        val updated = item.copy(
            status = newStatus,
            soldPrice = soldPrice,
            soldAt = soldAt,
            updatedAt = now,
            isDirty = true
        )
        dao.updateItem(updated)
    }

    suspend fun markItemShared(itemId: Int) {
        val itemWithPhotos = dao.getItemWithPhotosById(itemId) ?: return
        val updated = itemWithPhotos.item.copy(isShared = true)
        dao.updateItem(updated)
    }

    /**
     * 複数商品を「まとめ買い」出品として統合
     * 出品中（LISTED）のまままとめ買い商品を作成し、子商品の関係を保持
     */
    suspend fun mergeItems(
        mergedTitle: String,
        mergedDescription: String,
        totalSoldPrice: Int,
        newShippingCost: Int,
        newFeeRatePercent: Int,
        newExpenseCost: Int,
        childItemIds: List<Int>,
        asStatus: String = "LISTED"
    ): Long {
        val childItems = dao.getItemsWithPhotosByIds(childItemIds)
        val childTitles = childItems.joinToString(", ") { "【${it.item.title}】¥${it.item.price}" }
        val originalTotal = childItems.sumOf { it.item.price }
        val discount = (originalTotal - totalSoldPrice).coerceAtLeast(0)
        val maxChildLevel = childItems.maxOfOrNull { it.item.bundleLevel } ?: 0
        val totalChildCount = childItems.sumOf { if (it.item.mergedChildCount > 0) it.item.mergedChildCount else 1 }

        val now = System.currentTimeMillis()

        // 1. 新しいまとめ商品の作成 (デフォルトで LISTED 出品中)
        val parentItem = ItemEntity(
            title = mergedTitle,
            description = mergedDescription,
            status = asStatus,
            price = totalSoldPrice,
            soldPrice = if (isSoldFamilyStatus(asStatus)) totalSoldPrice else 0,
            shippingCost = newShippingCost,
            feeRatePercent = newFeeRatePercent,
            expenseCost = newExpenseCost,
            useCustomSettings = true,
            mergedChildCount = totalChildCount,
            mergedChildTitles = childTitles,
            originalTotalBundlePrice = originalTotal,
            discountAmount = discount,
            bundleLevel = maxChildLevel + 1,
            createdAt = now,
            updatedAt = now,
            soldAt = if (isSoldFamilyStatus(asStatus)) now else null,
            syncUuid = java.util.UUID.randomUUID().toString(),
            deletedAt = null,
            isDirty = true
        )
        val parentId = dao.insertItem(parentItem).toInt()

        // 2. 元の個別商品のステータスを「MERGED」に更新（通常一覧から除外、履歴保持）
        dao.markItemsMerged(childItemIds, parentId, now)

        // 3. 元の各商品の写真をすべて親商品に引き継ぎ（MD5ハッシュも継承）
        val parentPhotos = mutableListOf<PhotoEntity>()
        var order = 0
        for (child in childItems) {
            for (p in child.photos) {
                parentPhotos.add(
                    p.copy(
                        id = 0,
                        itemId = parentId,
                        displayOrder = order++,
                        md5Hash = p.md5Hash
                    )
                )
            }
        }
        dao.insertPhotos(parentPhotos)

        return parentId.toLong()
    }

    /**
     * まとめ買い出品を解除（バラす）
     * 子商品のステータスを LISTED に戻し、mergedParentId を解除。
     * 親商品とその写真レコードを削除し、親 syncUuid を完全削除記録へ追加する。
     */
    suspend fun unmergeItem(parentId: Int) {
        val parent = dao.getItemWithPhotosById(parentId) ?: return
        val now = System.currentTimeMillis()
        dao.restoreMergedChildren(parentId, now)
        dao.deletePhotosByItemId(parentId)
        dao.deleteItem(parent.item)
        if (parent.item.syncUuid.isNotBlank()) {
            addPendingDeletedUuid(parent.item.syncUuid)
        }
    }

    /**
     * まとめ商品の内包する子商品一覧を取得
     */
    suspend fun getMergedChildItemsWithPhotos(parentId: Int): List<ItemWithPhotos> {
        return dao.getMergedChildItemsWithPhotos(parentId)
    }

    /**
     * 論理削除（ゴミ箱へ移動）：写真ファイルは保持し、30日間復元可能にする
     */
    suspend fun moveToTrash(itemWithPhotos: ItemWithPhotos) {
        val now = System.currentTimeMillis()
        dao.softDeleteItem(itemWithPhotos.item.id, now, now)
        if (itemWithPhotos.item.mergedChildCount > 0) {
            dao.softDeleteChildrenByParent(itemWithPhotos.item.id, now, now)
        }
    }

    /**
     * 既存呼び出し互換のための削除エントリ（論理削除を実行）
     */
    suspend fun deleteItem(itemWithPhotos: ItemWithPhotos) {
        moveToTrash(itemWithPhotos)
    }

    /**
     * 写真を1枚単位で削除（残存PhotoEntityのID再採番を行わず、対象のみ削除＋未参照ファイル整理＋isDirty更新）
     */
    suspend fun deletePhotoById(photoId: Int): Boolean {
        return deletePhotosByIds(listOf(photoId)) > 0
    }

    /**
     * 複数写真を一括削除（残存PhotoEntityのID・md5Hash・isOriginalDeletedを保持したまま対象のみ削除）
     */
    suspend fun deletePhotosByIds(photoIds: List<Int>): Int {
        if (photoIds.isEmpty()) return 0
        val photos = photoIds.distinct().mapNotNull { dao.getPhotoById(it) }
        if (photos.isEmpty()) return 0
        val groupedByItem = photos.groupBy { it.itemId }
        val now = System.currentTimeMillis()
        var count = 0

        for ((itemId, photoGroup) in groupedByItem) {
            val itemWithPhotos = dao.getItemWithPhotosById(itemId) ?: continue
            val removeSet = photoGroup.map { it.id }.toSet()
            val toRemove = itemWithPhotos.photos.filter { it.id in removeSet }
            if (toRemove.isEmpty()) continue
            val remaining = itemWithPhotos.photos.filter { it.id !in removeSet }

            val keptPaths = remaining.flatMap {
                listOfNotNull(
                    it.filePath.takeIf { p -> p.isNotBlank() },
                    it.thumbnailPath?.takeIf { p -> p.isNotBlank() }
                )
            }.toSet()
            val otherReferencedPaths = dao.getPhotosExcludingItem(itemId).flatMap {
                listOfNotNull(
                    it.filePath.takeIf { p -> p.isNotBlank() },
                    it.thumbnailPath?.takeIf { p -> p.isNotBlank() }
                )
            }.toSet()

            for (targetPhoto in toRemove) {
                if (targetPhoto.filePath.isNotBlank() &&
                    !keptPaths.contains(targetPhoto.filePath) &&
                    !otherReferencedPaths.contains(targetPhoto.filePath)
                ) {
                    try {
                        File(targetPhoto.filePath).delete()
                    } catch (_: Exception) {
                    }
                }
                if (!targetPhoto.thumbnailPath.isNullOrBlank() &&
                    !keptPaths.contains(targetPhoto.thumbnailPath) &&
                    !otherReferencedPaths.contains(targetPhoto.thumbnailPath)
                ) {
                    try {
                        File(targetPhoto.thumbnailPath).delete()
                    } catch (_: Exception) {
                    }
                }
            }

            // レビュー 24-7: 削除した写真を未Push削除として記録する (送信されるまで復活防止の対象)
            for (removedPhoto in toRemove) {
                if (removedPhoto.md5Hash.isNotBlank()) {
                    recordPendingPhotoDeletion(itemWithPhotos.item.syncUuid, removedPhoto.md5Hash)
                }
            }
            dao.deletePhotosByIds(toRemove.map { it.id })
            if (remaining.isNotEmpty()) {
                val reindexed = remaining.mapIndexed { idx, photo ->
                    photo.copy(displayOrder = idx, updatedAt = now)
                }
                dao.updatePhotos(reindexed)
            }
            dao.updateItem(itemWithPhotos.item.copy(updatedAt = now, isDirty = true))
            count += toRemove.size
        }
        return count
    }

    /**
     * ゴミ箱から元の出品状態へ復元
     */
    suspend fun restoreFromTrash(itemId: Int) {
        val now = System.currentTimeMillis()
        dao.restoreFromTrash(itemId, now)
        dao.restoreChildrenByParent(itemId, now)
    }

    suspend fun moveItemsToTrash(itemIds: List<Int>): Int {
        var count = 0
        for (id in itemIds.distinct()) {
            val itemWithPhotos = dao.getItemWithPhotosById(id) ?: continue
            moveToTrash(itemWithPhotos)
            count++
        }
        return count
    }

    suspend fun restoreItemsFromTrash(itemIds: List<Int>): Int {
        var count = 0
        for (id in itemIds.distinct()) {
            restoreFromTrash(id)
            count++
        }
        return count
    }

    suspend fun permanentlyDeleteItemsFromTrash(itemIds: List<Int>): Int {
        var count = 0
        for (id in itemIds.distinct()) {
            val itemWithPhotos = dao.getItemWithPhotosById(id) ?: continue
            permanentlyDeleteItem(itemWithPhotos, recordTombstoneForSync = true)
            count++
        }
        return count
    }

    suspend fun batchUpdateItems(itemIds: List<Int>, spec: BatchEditSpec): Int {
        val now = System.currentTimeMillis()
        var updatedCount = 0
        for (id in itemIds.distinct()) {
            val existing = dao.getItemWithPhotosById(id)?.item ?: continue
            if (existing.deletedAt != null || existing.status == "MERGED") continue

            val nextPrice = when (spec.priceMode) {
                BatchPriceMode.NONE -> existing.price
                BatchPriceMode.FIXED -> spec.priceValue.coerceAtLeast(0)
                BatchPriceMode.DELTA_YEN -> (existing.price + spec.priceValue).coerceAtLeast(0)
                BatchPriceMode.DELTA_PERCENT -> (existing.price + (existing.price * spec.priceValue) / 100).coerceAtLeast(0)
            }

            val nextStatus = spec.newStatus ?: existing.status
            val nextSoldAt = if (isSoldFamilyStatus(nextStatus)) (existing.soldAt ?: now) else null
            val nextSoldPrice = when {
                !isSoldFamilyStatus(nextStatus) -> 0
                spec.priceMode != BatchPriceMode.NONE -> nextPrice
                existing.soldPrice > 0 -> existing.soldPrice
                else -> nextPrice
            }

            val updated = existing.copy(
                status = nextStatus,
                price = nextPrice,
                soldPrice = nextSoldPrice,
                soldAt = nextSoldAt,
                useCustomSettings = if (spec.applyCostPreset) spec.useCustomSettings else existing.useCustomSettings,
                feeRatePercent = if (spec.applyCostPreset) spec.feeRatePercent.coerceAtLeast(0) else existing.feeRatePercent,
                shippingCost = if (spec.applyCostPreset) spec.shippingCost.coerceAtLeast(0) else existing.shippingCost,
                expenseCost = if (spec.applyCostPreset) spec.expenseCost.coerceAtLeast(0) else existing.expenseCost,
                updatedAt = now,
                isDirty = true
            )
            dao.updateItem(updated)
            updatedCount++
        }
        return updatedCount
    }

    /**
     * 完全削除：他アイテムから参照されていない写真ファイルとDBレコードを物理削除する
     */
    suspend fun permanentlyDeleteItem(
        itemWithPhotos: ItemWithPhotos,
        recordTombstoneForSync: Boolean = true
    ) {
        val item = itemWithPhotos.item
        if (item.mergedChildCount > 0) {
            val children = dao.getMergedChildItemsWithPhotos(item.id)
            for (child in children) {
                deleteSingleItemPhysically(child, recordTombstoneForSync)
            }
        }
        deleteSingleItemPhysically(itemWithPhotos, recordTombstoneForSync)
    }

    private suspend fun deleteSingleItemPhysically(
        itemWithPhotos: ItemWithPhotos,
        recordTombstoneForSync: Boolean
    ) {
        val item = itemWithPhotos.item
        val otherPhotos = dao.getPhotosExcludingItem(item.id)
        val referencedPaths = HashSet<String>()
        for (p in otherPhotos) {
            if (p.filePath.isNotBlank()) referencedPaths.add(p.filePath)
            if (!p.thumbnailPath.isNullOrBlank()) referencedPaths.add(p.thumbnailPath)
        }

        val unreferencedPhotos = itemWithPhotos.photos.mapNotNull { p ->
            val origOk = p.filePath.isNotBlank() && !referencedPaths.contains(p.filePath)
            val thumbOk = !p.thumbnailPath.isNullOrBlank() && !referencedPaths.contains(p.thumbnailPath)
            if (!origOk && !thumbOk) {
                null
            } else {
                p.copy(
                    filePath = if (origOk) p.filePath else "",
                    thumbnailPath = if (thumbOk) p.thumbnailPath else null
                )
            }
        }
        if (unreferencedPhotos.isNotEmpty()) {
            photoStorageManager.deletePhotoFiles(unreferencedPhotos)
        }

        dao.deletePhotosByItemId(item.id)
        dao.deleteItem(item)
        if (recordTombstoneForSync && item.syncUuid.isNotBlank()) {
            addPendingDeletedUuid(item.syncUuid)
        }
    }

    /**
     * ゴミ箱を空にする（現在ゴミ箱にある全商品を完全削除）
     */
    suspend fun emptyTrash(): Int {
        val trashed = dao.getTrashedItemsWithPhotosDirect()
        for (item in trashed) {
            permanentlyDeleteItem(item, recordTombstoneForSync = true)
        }
        return trashed.size
    }

    /**
     * ゴミ箱投入から保持日数を超えた商品を自動完全削除
     */
    suspend fun purgeExpiredTrash(retentionDays: Int = 30): Int {
        val cutoffMs = System.currentTimeMillis() - (retentionDays * 24L * 60L * 60L * 1000L)
        val expired = dao.getExpiredTrashedItemsDirect(cutoffMs)
        if (expired.isEmpty()) return 0

        var purgedCount = 0
        for (itemWithPhotos in expired.filter { it.item.mergedParentId == null }) {
            if (dao.getItemWithPhotosById(itemWithPhotos.item.id) != null) {
                permanentlyDeleteItem(itemWithPhotos, recordTombstoneForSync = true)
                purgedCount++
            }
        }
        for (itemWithPhotos in expired.filter { it.item.mergedParentId != null }) {
            if (dao.getItemWithPhotosById(itemWithPhotos.item.id) != null) {
                permanentlyDeleteItem(itemWithPhotos, recordTombstoneForSync = true)
                purgedCount++
            }
        }
        return purgedCount
    }

    fun getPhotoStorageManager(): PhotoStorageManager = photoStorageManager
    fun getDao(): ItemDao = dao

    private fun isEmulator(): Boolean {
        return (android.os.Build.FINGERPRINT.startsWith("generic")
                || android.os.Build.FINGERPRINT.startsWith("unknown")
                || android.os.Build.MODEL.contains("google_sdk")
                || android.os.Build.MODEL.contains("Emulator")
                || android.os.Build.MODEL.contains("Android SDK built for x86")
                || android.os.Build.MANUFACTURER.contains("Genymotion")
                || (android.os.Build.BRAND.startsWith("generic") && android.os.Build.DEVICE.startsWith("generic"))
                || android.os.Build.PRODUCT.contains("sdk")
                || android.os.Build.PRODUCT.contains("google_sdk")
                || android.os.Build.PRODUCT.contains("sdk_gphone")
                || android.os.Build.HARDWARE.contains("goldfish")
                || android.os.Build.HARDWARE.contains("ranchu")
                || android.os.Build.HARDWARE.contains("vbox86"))
    }

    suspend fun seedInitialTestDataIfNeeded() {
        if (!isEmulator()) return
        val count = dao.getActiveItemsWithPhotosDirect().size
        if (count == 0) {
            val testItems = listOf(
                ItemEntity(title = "ヴィンテージ デニムジャケット", description = "美品のヴィンテージGジャンです。サイズM。", price = 8500, status = "LISTED"),
                ItemEntity(title = "レトロ スニーカー 27cm", description = "80年代風デザインの限定スニーカー。", price = 5400, status = "LISTED"),
                ItemEntity(title = "本革 レザーハンドバッグ", description = "上質な牛革を使用した高級バッグ。", price = 12800, status = "LISTED"),
                ItemEntity(title = "北欧風 チタン製マグカップ", description = "軽量でキャンプや登山に最適です。", price = 2200, status = "LISTED"),
                ItemEntity(title = "ワイヤレス ノイズキャンセリングイヤホン", description = "高音質でバッテリー長持ちモデル。", price = 7900, status = "LISTED"),
                ItemEntity(title = "ストリート系 キャップ 黒", description = "シンプルでどんな服装にも合わせやすい帽子。", price = 1800, status = "LISTED"),
                ItemEntity(title = "アンティーク 真鍮製卓上ランプ", description = "温かみのある光のインテリアランプ。", price = 4500, status = "LISTED"),
                ItemEntity(title = "ウール混 ざっくりニットセーター", description = "これからの季節にぴったりの暖かいセーター。", price = 3900, status = "LISTED"),
                ItemEntity(title = "クリア スマホショルダーポーチ", description = "お出かけに便利なクリアスマホポシェット。", price = 1500, status = "LISTED"),
                ItemEntity(title = "フレグランス オーデコロン 50ml", description = "爽やかなシトラス系の香水です。", price = 3200, status = "LISTED")
            )

            val colors = arrayOf("#4F46E5", "#059669", "#D97706", "#DC2626", "#7C3AED", "#2563EB", "#0D9488", "#CA8A04", "#475569", "#DB2777")

            for ((index, item) in testItems.withIndex()) {
                val itemId = dao.insertItem(item).toInt()
                val photoFile = File(context.filesDir, "test_seed_${itemId}.jpg")

                var success = false
                try {
                    val url = java.net.URL("https://picsum.photos/seed/item$index/300/300")
                    val connection = url.openConnection() as java.net.HttpURLConnection
                    connection.connectTimeout = 3000
                    connection.readTimeout = 3000
                    connection.inputStream.use { input ->
                        photoFile.outputStream().use { output ->
                            input.copyTo(output)
                        }
                    }
                    if (photoFile.exists() && photoFile.length() > 0) {
                        success = true
                    }
                } catch (e: Exception) {}

                if (!success) {
                    val bitmap = android.graphics.Bitmap.createBitmap(300, 300, android.graphics.Bitmap.Config.RGB_565)
                    val canvas = android.graphics.Canvas(bitmap)
                    canvas.drawColor(android.graphics.Color.parseColor(colors[index % colors.size]))
                    try {
                        photoFile.outputStream().use { out ->
                            bitmap.compress(android.graphics.Bitmap.CompressFormat.JPEG, 85, out)
                        }
                    } catch (e: Exception) {}
                }

                val md5 = photoStorageManager.calculateMd5(photoFile)
                val settings = defaultSettings.value
                val thumbPath = photoStorageManager.createThumbnail(
                    photoFile,
                    settings.thumbnailMaxDimension,
                    settings.thumbnailQuality
                )

                val photo = com.example.myapplication.data.local.PhotoEntity(
                    itemId = itemId,
                    filePath = photoFile.absolutePath,
                    thumbnailPath = thumbPath ?: photoFile.absolutePath,
                    md5Hash = md5,
                    displayOrder = 0,
                    syncStatus = "PENDING"
                )
                dao.insertPhotos(listOf(photo))
            }
        }
    }
}
