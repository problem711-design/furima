package com.example.myapplication.data

import android.content.Context
import android.net.Uri
import android.util.Log
import androidx.room.withTransaction
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
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File
import java.util.concurrent.ConcurrentHashMap

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

    // DB読み書きの短いクリティカルセクション専用の排他ロック (画像I/O・ネットワーク通信では保持しない)
    private val dbMutationMutex = Mutex()
    private val editingItemCounts = ConcurrentHashMap<Int, Int>()

    val defaultSettings: StateFlow<DefaultSettings> = defaultSettingsManager.settings

    private val syncTombstonePrefs =
        context.getSharedPreferences("flea_market_sync_tombstones", Context.MODE_PRIVATE)

    fun setItemEditingActive(itemId: Int, active: Boolean) {
        if (itemId <= 0) return
        if (active) {
            editingItemCounts.compute(itemId) { _, count -> (count ?: 0) + 1 }
        } else {
            editingItemCounts.computeIfPresent(itemId) { _, count ->
                val next = count - 1
                if (next > 0) next else null
            }
        }
    }

    fun isItemEditingActive(itemId: Int): Boolean = (editingItemCounts[itemId] ?: 0) > 0

    suspend fun <T> withDbMutationLock(block: suspend () -> T): T = dbMutationMutex.withLock {
        block()
    }

    suspend fun <T> withDbMutationTransaction(block: suspend () -> T): T = dbMutationMutex.withLock {
        db.withTransaction {
            block()
        }
    }

    suspend fun isFilePathProtectedByEditingOrDirty(filePath: String): Boolean {
        if (filePath.isBlank()) return false
        val refs = dao.getPhotosReferencingPath(filePath)
        for (ref in refs) {
            if (isItemEditingActive(ref.itemId)) return true
            val item = dao.getItemWithPhotosById(ref.itemId)?.item
            if (item == null || item.isDirty || item.deletedAt != null) return true
        }
        return false
    }

    // レビュー §83-4: 直近のゴースト浄化の内訳 (同期進捗メッセージでの UI 通知用)。
    // PcServerClient.syncDeltaToPcServer が消費して表示し、表示後にクリアする。
    @Volatile var lastGhostCleanupGhostCount: Int = 0
        private set
    @Volatile var lastGhostCleanupUnrecoverableCount: Int = 0
        private set

    /** 浄化内訳を 1 回だけ取り出す (取り出すと 0 に戻る)。 */
    fun consumeGhostCleanupSummary(): Pair<Int, Int> {
        val ghost = lastGhostCleanupGhostCount
        val unrecoverable = lastGhostCleanupUnrecoverableCount
        lastGhostCleanupGhostCount = 0
        lastGhostCleanupUnrecoverableCount = 0
        return ghost to unrecoverable
    }

    /**
     * ローカル DB に残存する空 MD5 の壊れ写真行を安全に除去する。
     * - 完全ゴースト: 実ファイルもサムネイルも存在しない行。
     * - 復旧不能行 (§83-4): 原本ファイルが消失し MD5 も未確定のまま、サムネイルだけが残った行。
     *   原本が無い以上ハッシュを確定できず、未同期カウントに残り続けると自動同期が
     *   無限に再起動するため、隔離せず浄化し、件数は UI 通知用に記録する。
     * 実体ファイル (原本) が存在する写真には絶対に触れない。編集中アイテムも保護する。
     */
    suspend fun cleanupGhostBlankMd5Photos(): Int = withContext(Dispatchers.IO) {
        val orphanThumbPaths = mutableListOf<String>()
        val removedTotal = withDbMutationTransaction {
            val candidates = dao.getBlankMd5Photos()
            if (candidates.isEmpty()) return@withDbMutationTransaction 0

            val ghostIds = mutableListOf<Int>()
            val unrecoverableIds = mutableListOf<Int>()
            val affectedItemIds = mutableSetOf<Int>()
            for (photo in candidates) {
                if (photo.md5Hash.isNotBlank()) continue
                if (isItemEditingActive(photo.itemId)) continue
                val origExists = photo.filePath.isNotBlank() &&
                    File(photo.filePath).let { it.exists() && it.isFile && it.length() > 0L }
                val thumbExists = !photo.thumbnailPath.isNullOrBlank() &&
                    File(photo.thumbnailPath).let { it.exists() && it.isFile && it.length() > 0L }
                if (!origExists && !thumbExists) {
                    ghostIds.add(photo.id)
                    affectedItemIds.add(photo.itemId)
                } else if (!origExists && thumbExists) {
                    // §83-4: 原本消失・MD5 未確定・サムネイルのみ生存 = 復旧不能行として浄化する
                    unrecoverableIds.add(photo.id)
                    affectedItemIds.add(photo.itemId)
                    val thumbPath = photo.thumbnailPath
                    if (!thumbPath.isNullOrBlank() &&
                        dao.getPhotosReferencingPath(thumbPath).none { it.id != photo.id }
                    ) {
                        orphanThumbPaths.add(thumbPath)
                    }
                }
            }
            val allIds = ghostIds + unrecoverableIds
            if (allIds.isEmpty()) return@withDbMutationTransaction 0

            dao.deletePhotosByIds(allIds)
            val now = System.currentTimeMillis()
            for (affectedItemId in affectedItemIds) {
                val remaining = dao.getPhotosByItemId(affectedItemId)
                if (remaining.isNotEmpty()) {
                    val reindexed = remaining.mapIndexed { idx, p ->
                        p.copy(displayOrder = idx, updatedAt = now)
                    }
                    dao.updatePhotos(reindexed)
                }
            }
            if (ghostIds.isNotEmpty()) {
                Log.w("PHOTO_INTEGRITY", "Cleaned up ${ghostIds.size} orphan blank-MD5 ghost photo(s) across ${affectedItemIds.size} item(s)")
            }
            if (unrecoverableIds.isNotEmpty()) {
                Log.w("PHOTO_INTEGRITY", "Cleaned up ${unrecoverableIds.size} unrecoverable blank-MD5 photo row(s) (original missing, thumbnail only)")
                lastGhostCleanupUnrecoverableCount += unrecoverableIds.size
            }
            if (ghostIds.isNotEmpty()) {
                lastGhostCleanupGhostCount += ghostIds.size
            }
            allIds.size
        }
        // 他の写真行から参照されていないサムネイル実ファイルは、行の除去に合わせて破棄する
        for (thumbPath in orphanThumbPaths) {
            try {
                File(thumbPath).delete()
            } catch (_: Exception) {
            }
        }
        removedTotal
    }

    /**
     * §83-4: 未同期写真数の判定前にゴースト浄化を行い、壊れ行で常時 > 0 にならないようにする。
     * 自動同期・再接続ハンドシェイクの判定はこちらを使う。
     */
    suspend fun getUnsyncedPhotosCountAfterGhostCleanup(): Int {
        cleanupGhostBlankMd5Photos()
        return dao.getUnsyncedPhotosCount()
    }

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

    private sealed class PreparedSavePhoto {
        data class ExistingRef(
            val matchedPhotoId: Int,
            val origLookupPath: String,
            val resolvedMd5: String,
            val generatedThumbPath: String?,
            val fallbackEntity: PhotoEntity
        ) : PreparedSavePhoto()

        data class NewPhoto(
            val filePath: String,
            val thumbnailPath: String?,
            val md5Hash: String
        ) : PreparedSavePhoto()
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
        existingFilePaths: List<String> = emptyList(),
        onSkippedInvalidPhotos: (Int) -> Unit = {}
    ): Long = withContext(Dispatchers.IO) {
        // 1. 重いファイルI/O（画像コピー・リサイズ・サムネ生成・MD5計算）を DB トランザクションの前に完了させる
        val currentSettings = defaultSettings.value
        val existingPhotosSnapshot = if (id != 0) dao.getPhotosByItemId(id) else emptyList()
        val snapByFilePath = existingPhotosSnapshot.filter { it.filePath.isNotBlank() }.associateBy { it.filePath }
        val snapByThumbPath = existingPhotosSnapshot.filter { !it.thumbnailPath.isNullOrBlank() }.associateBy { it.thumbnailPath!! }
        val snapByThumbName = existingPhotosSnapshot.filter { !it.thumbnailPath.isNullOrBlank() }
            .associateBy { File(it.thumbnailPath!!).name }

        val preparedPhotos = mutableListOf<PreparedSavePhoto>()
        var skippedInvalidCount = 0

        for (path in existingFilePaths) {
            if (path.isBlank()) {
                skippedInvalidCount++
                continue
            }
            val thumbFallbackName = "THUMB_${File(path).name}"
            val existing = snapByFilePath[path] ?: snapByThumbPath[path] ?: snapByThumbName[thumbFallbackName]
            if (existing != null) {
                var resolvedMd5 = existing.md5Hash.trim()
                val readableFile = existing.displayFile
                    ?: File(path).takeIf { it.exists() && it.isFile && it.length() > 0L }
                if (resolvedMd5.isBlank() && readableFile != null) {
                    resolvedMd5 = photoStorageManager.calculateMd5(readableFile)
                }
                if (resolvedMd5.isBlank()) {
                    Log.w("PHOTO_INTEGRITY", "saveItem: skipped existing photo with blank MD5 & missing file (id=${existing.id}, path=$path)")
                    skippedInvalidCount++
                    continue
                }
                val generatedThumb = if (existing.thumbnailPath.isNullOrBlank() && readableFile != null) {
                    photoStorageManager.createThumbnail(
                        readableFile,
                        currentSettings.thumbnailMaxDimension,
                        currentSettings.thumbnailQuality
                    )
                } else {
                    null
                }
                preparedPhotos.add(
                    PreparedSavePhoto.ExistingRef(
                        matchedPhotoId = existing.id,
                        origLookupPath = path,
                        resolvedMd5 = resolvedMd5,
                        generatedThumbPath = generatedThumb,
                        fallbackEntity = existing
                    )
                )
            } else {
                val file = File(path)
                if (!file.exists() || !file.isFile || file.length() <= 0L) {
                    Log.w("PHOTO_INTEGRITY", "saveItem: skipped missing/empty existingFilePath=$path")
                    skippedInvalidCount++
                    continue
                }
                val md5 = photoStorageManager.calculateMd5(file)
                if (md5.isBlank()) {
                    Log.w("PHOTO_INTEGRITY", "saveItem: skipped existingFilePath with blank MD5=$path")
                    skippedInvalidCount++
                    continue
                }
                val thumb = photoStorageManager.createThumbnail(
                    file,
                    currentSettings.thumbnailMaxDimension,
                    currentSettings.thumbnailQuality
                )
                preparedPhotos.add(
                    PreparedSavePhoto.NewPhoto(
                        filePath = path,
                        thumbnailPath = thumb,
                        md5Hash = md5
                    )
                )
            }
        }

        for (uri in imageUris) {
            val savedResult = photoStorageManager.savePhotoFromUri(
                title,
                uri,
                thumbQuality = currentSettings.thumbnailQuality,
                thumbMaxDim = currentSettings.thumbnailMaxDimension
            )
            if (savedResult != null) {
                val origFile = File(savedResult.first)
                val md5 = if (origFile.exists() && origFile.isFile && origFile.length() > 0L) {
                    photoStorageManager.calculateMd5(origFile)
                } else {
                    ""
                }
                if (md5.isBlank()) {
                    Log.w("PHOTO_INTEGRITY", "saveItem: URI save yielded empty/unreadable file, discarding ${savedResult.first}")
                    try { origFile.delete() } catch (_: Exception) {}
                    try { savedResult.second?.let { File(it).delete() } } catch (_: Exception) {}
                    skippedInvalidCount++
                    continue
                }
                preparedPhotos.add(
                    PreparedSavePhoto.NewPhoto(
                        filePath = savedResult.first,
                        thumbnailPath = savedResult.second,
                        md5Hash = md5
                    )
                )
            } else {
                skippedInvalidCount++
                Log.w("PHOTO_INTEGRITY", "saveItem: savePhotoFromUri returned null for uri=$uri")
            }
        }

        // 2. DB 読み書きは dbMutationMutex + Room Transaction で原子的に実行
        val filesToDeleteAfterTx = mutableListOf<File>()
        val removedMd5sToRecord = mutableListOf<String>()
        val savedMd5sToRelease = mutableListOf<String>()
        lateinit var preservedUuid: String

        val resolvedItemId = withDbMutationTransaction {
            val existingItem = if (id != 0) dao.getItemWithPhotosById(id)?.item else null
            val now = System.currentTimeMillis()
            val soldAt = if (isSoldFamilyStatus(status)) {
                existingItem?.soldAt ?: now
            } else {
                null
            }
            preservedUuid = existingItem?.syncUuid?.takeIf { it.isNotBlank() }
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

            val latestExistingPhotos = if (id != 0) dao.getPhotosByItemId(itemId) else emptyList()
            val latestById = latestExistingPhotos.associateBy { it.id }
            val latestByFilePath = latestExistingPhotos.filter { it.filePath.isNotBlank() }.associateBy { it.filePath }
            val latestByThumbPath = latestExistingPhotos.filter { !it.thumbnailPath.isNullOrBlank() }.associateBy { it.thumbnailPath!! }
            val latestByThumbName = latestExistingPhotos.filter { !it.thumbnailPath.isNullOrBlank() }
                .associateBy { File(it.thumbnailPath!!).name }

            val newPhotoEntities = mutableListOf<PhotoEntity>()
            var orderIndex = 0
            for (prepared in preparedPhotos) {
                when (prepared) {
                    is PreparedSavePhoto.ExistingRef -> {
                        val thumbFallbackName = "THUMB_${File(prepared.origLookupPath).name}"
                        val latest = latestById[prepared.matchedPhotoId]
                            ?: latestByFilePath[prepared.origLookupPath]
                            ?: latestByThumbPath[prepared.origLookupPath]
                            ?: latestByThumbName[thumbFallbackName]
                            ?: prepared.fallbackEntity
                        val finalMd5 = latest.md5Hash.trim().ifBlank { prepared.resolvedMd5 }
                        val finalThumb = latest.thumbnailPath?.takeIf { it.isNotBlank() } ?: prepared.generatedThumbPath
                        newPhotoEntities.add(
                            latest.copy(
                                itemId = itemId,
                                thumbnailPath = finalThumb,
                                md5Hash = finalMd5,
                                displayOrder = orderIndex++,
                                updatedAt = now
                            )
                        )
                    }
                    is PreparedSavePhoto.NewPhoto -> {
                        newPhotoEntities.add(
                            PhotoEntity(
                                itemId = itemId,
                                filePath = prepared.filePath,
                                thumbnailPath = prepared.thumbnailPath,
                                md5Hash = prepared.md5Hash,
                                displayOrder = orderIndex++,
                                updatedAt = now
                            )
                        )
                    }
                }
            }

            if (id != 0 && latestExistingPhotos.isNotEmpty()) {
                val keptIds = newPhotoEntities.mapNotNull { it.id.takeIf { pid -> pid != 0 } }.toSet()
                val keptPaths = newPhotoEntities.flatMap {
                    listOfNotNull(
                        it.filePath.takeIf { p -> p.isNotBlank() },
                        it.thumbnailPath?.takeIf { p -> p.isNotBlank() }
                    )
                }.toSet()
                val removedPhotos = latestExistingPhotos.filter { old ->
                    !keptIds.contains(old.id) && (
                        (old.filePath.isNotBlank() && !keptPaths.contains(old.filePath)) ||
                            (!old.thumbnailPath.isNullOrBlank() && !keptPaths.contains(old.thumbnailPath))
                        )
                }
                if (removedPhotos.isNotEmpty()) {
                    for (removed in removedPhotos) {
                        if (removed.md5Hash.isNotBlank()) {
                            removedMd5sToRecord.add(removed.md5Hash)
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
                            filesToDeleteAfterTx.add(File(removed.filePath))
                        }
                        if (!removed.thumbnailPath.isNullOrBlank() &&
                            !keptPaths.contains(removed.thumbnailPath) &&
                            !otherReferencedPaths.contains(removed.thumbnailPath)
                        ) {
                            filesToDeleteAfterTx.add(File(removed.thumbnailPath))
                        }
                    }
                }
            }

            dao.deletePhotosByItemId(itemId)
            if (newPhotoEntities.isNotEmpty()) {
                dao.insertPhotos(newPhotoEntities)
            }
            for (savedPhoto in newPhotoEntities) {
                if (savedPhoto.md5Hash.isNotBlank()) {
                    savedMd5sToRelease.add(savedPhoto.md5Hash)
                }
            }
            itemId
        }

        // 3. トランザクション確定後の tombstone 更新・未参照ファイル削除・異常スキップ通知
        for (md5 in removedMd5sToRecord) {
            recordPendingPhotoDeletion(preservedUuid, md5)
        }
        for (md5 in savedMd5sToRelease) {
            releasePendingPhotoDeletion(preservedUuid, md5)
        }
        for (f in filesToDeleteAfterTx) {
            try {
                if (f.exists()) f.delete()
            } catch (_: Exception) {
            }
        }
        if (skippedInvalidCount > 0) {
            onSkippedInvalidPhotos(skippedInvalidCount)
        }

        return@withContext resolvedItemId.toLong()
    }

    suspend fun removePhotoFromItem(itemId: Int, photoId: Int): Boolean {
        val filesToDelete = mutableListOf<File>()
        var targetMd5 = ""
        var itemUuid = ""
        val removed = withDbMutationTransaction {
            val itemWithPhotos = dao.getItemWithPhotosById(itemId) ?: return@withDbMutationTransaction false
            val targetPhoto = itemWithPhotos.photos.firstOrNull { it.id == photoId } ?: return@withDbMutationTransaction false
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
                filesToDelete.add(File(targetPhoto.filePath))
            }
            if (!targetPhoto.thumbnailPath.isNullOrBlank() &&
                !keptPaths.contains(targetPhoto.thumbnailPath) &&
                !otherReferencedPaths.contains(targetPhoto.thumbnailPath)
            ) {
                filesToDelete.add(File(targetPhoto.thumbnailPath))
            }

            targetMd5 = targetPhoto.md5Hash
            itemUuid = itemWithPhotos.item.syncUuid
            val now = System.currentTimeMillis()
            val reindexed = remaining.mapIndexed { idx, photo ->
                photo.copy(displayOrder = idx, updatedAt = now)
            }
            dao.deletePhotosByItemId(itemId)
            if (reindexed.isNotEmpty()) {
                dao.insertPhotos(reindexed)
            }
            dao.updateItem(itemWithPhotos.item.copy(updatedAt = now, isDirty = true))
            true
        }
        if (removed) {
            if (targetMd5.isNotBlank()) {
                recordPendingPhotoDeletion(itemUuid, targetMd5)
            }
            for (f in filesToDelete) {
                try {
                    if (f.exists()) f.delete()
                } catch (_: Exception) {
                }
            }
        }
        return removed
    }

    suspend fun updateItemStatus(item: ItemEntity, newStatus: String) {
        withDbMutationTransaction {
            val fresh = dao.getItemWithPhotosById(item.id)?.item ?: item
            val now = System.currentTimeMillis()
            // レビュー §30.4-3: 売却系の出入りを他経路 (保存・一括編集) と揃える。
            // 売却系から外れたら売却価格・日時を消し、入った場合は未設定の売却価格を出品価格で補う。
            val inFamily = isSoldFamilyStatus(newStatus)
            val soldAt = if (inFamily) (fresh.soldAt ?: now) else null
            val soldPrice = when {
                !inFamily -> 0
                fresh.soldPrice > 0 -> fresh.soldPrice
                else -> fresh.price
            }
            val updated = fresh.copy(
                status = newStatus,
                soldPrice = soldPrice,
                soldAt = soldAt,
                updatedAt = now,
                isDirty = true
            )
            dao.updateItem(updated)
        }
    }

    suspend fun markItemShared(itemId: Int) {
        withDbMutationLock {
            val itemWithPhotos = dao.getItemWithPhotosById(itemId) ?: return@withDbMutationLock
            val updated = itemWithPhotos.item.copy(isShared = true)
            dao.updateItem(updated)
        }
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
    ): Long = withDbMutationTransaction {
        val distinctIds = childItemIds.distinct()
        require(distinctIds.size >= 2) { "まとめ買いには2点以上の異なる商品が必要です" }
        require(asStatus in setOf("DRAFT", "DRAFT_DONE", "LISTED", "SHIPPING", "SOLD")) { "無効なステータスです: $asStatus" }
        val childItems = dao.getItemsWithPhotosByIds(distinctIds)
        require(childItems.size == distinctIds.size) { "対象の商品の一部が見つかりません" }
        for (c in childItems) {
            require(c.item.deletedAt == null) { "ゴミ箱内の商品はまとめられません: ${c.item.title}" }
            require(c.item.mergedParentId == null && c.item.status != "MERGED") { "既にまとめ済みの商品は選択できません: ${c.item.title}" }
            require(!isSoldFamilyStatus(c.item.status)) { "売却済み・発送待ちの商品はまとめられません: ${c.item.title}" }
        }
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
        dao.markItemsMerged(distinctIds, parentId, now)

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

        parentId.toLong()
    }

    /**
     * まとめ買い出品を解除（バラす）
     * 子商品のステータスを LISTED に戻し、mergedParentId を解除。
     * 親商品とその写真レコードを削除し、親 syncUuid を完全削除記録へ追加する。
     */
    suspend fun unmergeItem(parentId: Int) {
        val deletedUuid = withDbMutationTransaction {
            val parent = dao.getItemWithPhotosById(parentId) ?: return@withDbMutationTransaction ""
            val now = System.currentTimeMillis()
            dao.restoreMergedChildren(parentId, now)
            dao.deletePhotosByItemId(parentId)
            dao.deleteItem(parent.item)
            parent.item.syncUuid
        }
        if (deletedUuid.isNotBlank()) {
            addPendingDeletedUuid(deletedUuid)
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
        withDbMutationTransaction {
            val now = System.currentTimeMillis()
            dao.softDeleteItem(itemWithPhotos.item.id, now, now)
            if (itemWithPhotos.item.mergedChildCount > 0) {
                dao.softDeleteChildrenByParent(itemWithPhotos.item.id, now, now)
            }
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
        val filesToDelete = mutableListOf<File>()
        val pendingDeletes = mutableListOf<Pair<String, String>>()

        val count = withDbMutationTransaction {
            val photos = photoIds.distinct().mapNotNull { dao.getPhotoById(it) }
            if (photos.isEmpty()) return@withDbMutationTransaction 0
            val groupedByItem = photos.groupBy { it.itemId }
            val now = System.currentTimeMillis()
            var removedTotal = 0

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
                        filesToDelete.add(File(targetPhoto.filePath))
                    }
                    if (!targetPhoto.thumbnailPath.isNullOrBlank() &&
                        !keptPaths.contains(targetPhoto.thumbnailPath) &&
                        !otherReferencedPaths.contains(targetPhoto.thumbnailPath)
                    ) {
                        filesToDelete.add(File(targetPhoto.thumbnailPath))
                    }
                    if (targetPhoto.md5Hash.isNotBlank()) {
                        pendingDeletes.add(itemWithPhotos.item.syncUuid to targetPhoto.md5Hash)
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
                removedTotal += toRemove.size
            }
            removedTotal
        }

        for ((uuid, md5) in pendingDeletes) {
            recordPendingPhotoDeletion(uuid, md5)
        }
        for (f in filesToDelete) {
            try {
                if (f.exists()) f.delete()
            } catch (_: Exception) {
            }
        }
        return count
    }

    /**
     * ゴミ箱から元の出品状態へ復元
     */
    suspend fun restoreFromTrash(itemId: Int) {
        withDbMutationTransaction {
            val now = System.currentTimeMillis()
            dao.restoreFromTrash(itemId, now)
            dao.restoreChildrenByParent(itemId, now)
        }
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
        return withDbMutationTransaction {
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
            updatedCount
        }
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
        val unreferencedPhotos = withDbMutationTransaction {
            val fresh = dao.getItemWithPhotosById(item.id)
            val targetItem = fresh?.item ?: item
            val targetPhotos = fresh?.photos ?: itemWithPhotos.photos
            val otherPhotos = dao.getPhotosExcludingItem(targetItem.id)
            val referencedPaths = HashSet<String>()
            for (p in otherPhotos) {
                if (p.filePath.isNotBlank()) referencedPaths.add(p.filePath)
                if (!p.thumbnailPath.isNullOrBlank()) referencedPaths.add(p.thumbnailPath)
            }

            val toDelete = targetPhotos.mapNotNull { p ->
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
            dao.deletePhotosByItemId(targetItem.id)
            dao.deleteItem(targetItem)
            toDelete
        }
        if (unreferencedPhotos.isNotEmpty()) {
            photoStorageManager.deletePhotoFiles(unreferencedPhotos)
        }
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
