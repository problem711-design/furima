package com.example.myapplication.data.server

import android.content.Context
import android.util.Log
import android.graphics.BitmapFactory
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.PhotoStorageManager
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.local.isSoldFamilyStatus
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Request
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.security.MessageDigest

internal class ServerItemMergeEngine(
    private val context: Context,
    private val transport: PcServerTransport,
    private val repository: FleaMarketRepository,
    private val photoStorageManager: PhotoStorageManager
) {
    private val client get() = transport.client

    private fun getSavedServerUrl(): String = transport.getSavedServerUrl()
    private fun getSavedAuthToken(): String = transport.getSavedAuthToken()
    private suspend fun fetchPermanentlyDeletedUuids(baseUrl: String, token: String): Set<String> =
        transport.fetchPermanentlyDeletedUuids(baseUrl, token)

    suspend fun fetchServerItems(baseUrl: String, token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false

            val permanentlyDeletedUuids = fetchPermanentlyDeletedUuids(cleanUrl, token)

            val url = "$cleanUrl/api/items"
            val reqBuilder = Request.Builder()
                .url(url)
                .addHeader("X-Client-Type", "phone")
                .get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")

            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val bodyStr = response.body?.string() ?: return@withContext false
                val jsonArray = org.json.JSONArray(bodyStr)
                mergeServerItemsFromJson(
                    jsonArray = jsonArray,
                    permanentlyDeletedUuids = permanentlyDeletedUuids,
                    baseUrl = cleanUrl,
                    token = token,
                    repairThumbnails = true
                )
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "fetchServerItems 例外: ${e.message}", e)
            false
        }
    }

    /**
     * レビュー 24-7: isDirty 保護で商品マージをスキップする際の、写真だけの加算マージ。
     * サーバー側の写真のうち、ローカルに無く、未Push削除の記録にも無いものだけを追加する。
     * ローカル写真の削除・上書き、商品項目の変更は一切行わない。
     * 実ファイルの解決は通常マージと同じ規則 (同一 MD5 の既存写真から流用) に従う。
     */
    private suspend fun mergeServerPhotosAdditively(
        obj: JSONObject,
        itemId: Int,
        itemSyncUuid: String,
        allExistingPhotos: List<PhotoEntity>
    ): Boolean {
        val photosArray = obj.optJSONArray("photos") ?: return false
        if (photosArray.length() == 0) return false
        val dao = repository.getDao()
        val currentPhotos = dao.getPhotosByItemId(itemId)
        val localHashes = currentPhotos.map { it.md5Hash.trim().lowercase() }.toSet()
        val pendingDeletions = repository.getPendingPhotoDeletions(itemSyncUuid)
        var nextOrder = (currentPhotos.maxOfOrNull { it.displayOrder } ?: -1) + 1
        val additions = mutableListOf<PhotoEntity>()
        for (pIdx in 0 until photosArray.length()) {
            val pObj = photosArray.getJSONObject(pIdx)
            val md5 = pObj.optString("md5Hash", "").trim()
            if (md5.isBlank()) continue
            val md5Lower = md5.lowercase()
            if (localHashes.contains(md5Lower)) continue
            if (pendingDeletions.contains(md5Lower)) continue

            var localFilePath = ""
            var localThumbPath: String? = null
            var isOrigMissing = false
            val matched = allExistingPhotos.firstOrNull {
                it.md5Hash.equals(md5, ignoreCase = true) &&
                    ((it.filePath.isNotBlank() && File(it.filePath).exists()) ||
                        (!it.thumbnailPath.isNullOrBlank() && File(it.thumbnailPath).exists()))
            }
            if (matched != null) {
                val origFile = if (matched.filePath.isNotBlank()) File(matched.filePath) else null
                val thumbFile = if (!matched.thumbnailPath.isNullOrBlank()) File(matched.thumbnailPath) else null
                val origIsReal = origFile != null && origFile.exists() &&
                    !matched.filePath.equals(matched.thumbnailPath, ignoreCase = true)
                if (origIsReal) {
                    localFilePath = matched.filePath
                    localThumbPath = matched.thumbnailPath
                    isOrigMissing = false
                } else if (thumbFile != null && thumbFile.exists()) {
                    localFilePath = matched.thumbnailPath ?: ""
                    localThumbPath = matched.thumbnailPath
                    isOrigMissing = true
                } else {
                    localFilePath = matched.filePath
                    localThumbPath = matched.thumbnailPath
                    isOrigMissing = matched.isOriginalDeleted
                }
            }
            val hasRealOriginal = localFilePath.isNotBlank() &&
                !localFilePath.equals(localThumbPath, ignoreCase = true) &&
                File(localFilePath).exists()
            additions.add(
                PhotoEntity(
                    itemId = itemId,
                    filePath = localFilePath,
                    thumbnailPath = localThumbPath,
                    isOriginalDeleted = isOrigMissing || (!hasRealOriginal && !localThumbPath.isNullOrBlank()),
                    isPcSynced = true,
                    md5Hash = md5,
                    syncStatus = "SYNCED",
                    displayOrder = nextOrder++
                )
            )
        }
        if (additions.isNotEmpty()) {
            dao.insertPhotos(additions)
            return true
        }
        return false
    }

    /**
     * サーバーJSON配列を syncUuid 基準・isDirty/updatedAt 保護・ID衝突回避でローカルRoomへマージする
     */
    internal suspend fun mergeServerItemsFromJson(
        jsonArray: org.json.JSONArray,
        permanentlyDeletedUuids: Set<String> = emptySet(),
        baseUrl: String = "",
        token: String = "",
        repairThumbnails: Boolean = true
    ): Boolean = withContext(Dispatchers.IO) {
        val dao = repository.getDao()
        val pendingDeletedUuids = repository.getPendingDeletedUuids()
        val allExistingPhotos = dao.getAllItemsWithPhotosForSync().flatMap { it.photos }

        // 1. サーバー側で完全削除されたUUIDをローカルからも物理削除（未送信ローカル変更・編集中でない場合）
        if (permanentlyDeletedUuids.isNotEmpty()) {
            for (delUuid in permanentlyDeletedUuids) {
                val cleanUuid = delUuid.trim()
                if (cleanUuid.isBlank()) continue
                val existingByUuid = dao.getItemWithPhotosBySyncUuid(cleanUuid)
                if (existingByUuid != null &&
                    !existingByUuid.item.isDirty &&
                    !repository.isItemEditingActive(existingByUuid.item.id)
                ) {
                    repository.permanentlyDeleteItem(existingByUuid, recordTombstoneForSync = false)
                }
            }
        }

        // 2. JSONオブジェクトを抽出＆サーバーID -> syncUuid マップ構築
        val rawObjects = mutableListOf<JSONObject>()
        val serverIdToUuid = mutableMapOf<Int, String>()
        for (i in 0 until jsonArray.length()) {
            val obj = jsonArray.optJSONObject(i) ?: continue
            rawObjects.add(obj)
            val sId = obj.optInt("id", 0)
            val sUuid = obj.optString("syncUuid", "").trim()
            if (sId > 0 && sUuid.isNotBlank()) {
                serverIdToUuid[sId] = sUuid
            }
        }

        // 3. 親商品を先に処理して mergedParentId のローカルID引き当てを保証
        val sortedObjects = rawObjects.sortedBy { obj ->
            val hasParent = obj.has("mergedParentId") && !obj.isNull("mergedParentId") && obj.optInt("mergedParentId", 0) > 0
            if (hasParent) 1 else 0
        }

        for (obj in sortedObjects) {
            val serverId = obj.optInt("id", 0)
            val serverSyncUuid = obj.optString("syncUuid", "").trim()
            if (serverId <= 0 && serverSyncUuid.isBlank()) continue

            if (serverSyncUuid.isNotBlank() &&
                (permanentlyDeletedUuids.contains(serverSyncUuid) || pendingDeletedUuids.contains(serverSyncUuid))
            ) {
                continue
            }

            var itemIdToRepairThumbnails: Int? = null
            var savedTargetItemIdForRepair: Int? = null
            repository.withDbMutationTransaction {
                val byUuid = if (serverSyncUuid.isNotBlank()) dao.getItemWithPhotosBySyncUuid(serverSyncUuid) else null
                val byId = if (serverId > 0) dao.getItemWithPhotosById(serverId) else null
                val isIdCollision = (byUuid == null && byId != null && serverSyncUuid.isNotBlank() &&
                    byId.item.syncUuid.isNotBlank() && !byId.item.syncUuid.equals(serverSyncUuid, ignoreCase = true))

                val existing = byUuid ?: if (!isIdCollision) byId else null

                val serverUpdatedAt = if (obj.has("updatedAt") && !obj.isNull("updatedAt")) obj.optLong("updatedAt", 0L) else 0L

                // §83-7: 編集中アイテムは Pull 反映自体を保留する (saveItem の
                // トランザクション外画像 I/O 中に写真・フィールドが置換されるのを防ぐ)。
                // 次回同期で改めて反映される。
                if (existing != null && repository.isItemEditingActive(existing.item.id)) {
                    return@withDbMutationTransaction
                }

                // isDirty 保護：ローカルに未送信の変更があり、ローカル updatedAt がサーバーより新しい／同等なら上書きしない
                if (existing != null && existing.item.isDirty &&
                    (serverUpdatedAt <= 0L || existing.item.updatedAt >= serverUpdatedAt)
                ) {
                    val addedPhotos = mergeServerPhotosAdditively(obj, existing.item.id, existing.item.syncUuid, allExistingPhotos)
                    if (addedPhotos) {
                        itemIdToRepairThumbnails = existing.item.id
                    }
                    return@withDbMutationTransaction
                }

                val isLegacyDeleted = obj.optBoolean("isDeleted", false)
                val rawDeletedAt = if (obj.has("deletedAt") && !obj.isNull("deletedAt")) obj.optLong("deletedAt", 0L).takeIf { it > 0L } else null
                val serverDeletedAt = when {
                    rawDeletedAt != null -> rawDeletedAt
                    isLegacyDeleted -> existing?.item?.deletedAt ?: serverUpdatedAt.takeIf { it > 0L } ?: System.currentTimeMillis()
                    else -> null
                }

                val title = obj.optString("title", "商品")
                val description = obj.optString("description", "")
                val status = obj.optString("status", "LISTED")
                val price = obj.optInt("price", 0)
                val soldPrice = obj.optInt("soldPrice", 0)

                val rawMergedParentId = if (obj.has("mergedParentId") && !obj.isNull("mergedParentId")) {
                    obj.optInt("mergedParentId", 0).takeIf { it > 0 }
                } else {
                    null
                }
                val rawMergedParentUuid = obj.optString("mergedParentSyncUuid", "").trim()
                    .ifBlank { rawMergedParentId?.let { serverIdToUuid[it] } ?: "" }
                val resolvedMergedParentId = when {
                    rawMergedParentUuid.isNotBlank() ->
                        dao.getItemWithPhotosBySyncUuid(rawMergedParentUuid)?.item?.id ?: rawMergedParentId
                    else -> rawMergedParentId
                }

                val mergedChildCount = obj.optInt("mergedChildCount", 0)
                val mergedChildTitles = obj.optString("mergedChildTitles", "")
                val originalTotalBundlePrice = obj.optInt("originalTotalBundlePrice", 0)
                val discountAmount = obj.optInt("discountAmount", 0)
                val bundleLevel = obj.optInt("bundleLevel", 0)

                val serverCreatedAt = if (obj.has("createdAt") && !obj.isNull("createdAt")) obj.optLong("createdAt", 0L).takeIf { it > 0L } else null
                val serverSoldAt = if (obj.has("soldAt") && !obj.isNull("soldAt")) obj.optLong("soldAt", 0L).takeIf { it > 0L } else null

                val resolvedUuid = serverSyncUuid.ifBlank {
                    existing?.item?.syncUuid?.takeIf { it.isNotBlank() } ?: java.util.UUID.randomUUID().toString()
                }

                val targetItemId: Int
                if (existing != null) {
                    targetItemId = existing.item.id
                    val resolvedCreatedAt = when {
                        serverCreatedAt != null && existing.item.createdAt > 0L -> minOf(serverCreatedAt, existing.item.createdAt)
                        serverCreatedAt != null -> serverCreatedAt
                        else -> existing.item.createdAt
                    }
                    val resolvedUpdatedAt = if (serverUpdatedAt > 0L) serverUpdatedAt else existing.item.updatedAt
                    val resolvedSoldAt = if (isSoldFamilyStatus(status)) {
                        serverSoldAt ?: existing.item.soldAt
                    } else {
                        null
                    }
                    val updated = existing.item.copy(
                        title = title,
                        description = description,
                        status = status,
                        price = price,
                        soldPrice = soldPrice,
                        mergedParentId = resolvedMergedParentId,
                        mergedChildCount = mergedChildCount,
                        mergedChildTitles = if (mergedChildTitles.isNotBlank()) mergedChildTitles else existing.item.mergedChildTitles,
                        originalTotalBundlePrice = if (originalTotalBundlePrice > 0) originalTotalBundlePrice else existing.item.originalTotalBundlePrice,
                        discountAmount = if (discountAmount > 0) discountAmount else existing.item.discountAmount,
                        bundleLevel = if (bundleLevel > 0) bundleLevel else existing.item.bundleLevel,
                        shippingCost = if (obj.has("shippingCost") && !obj.isNull("shippingCost")) obj.optInt("shippingCost", existing.item.shippingCost) else existing.item.shippingCost,
                        feeRatePercent = if (obj.has("feeRatePercent") && !obj.isNull("feeRatePercent")) obj.optInt("feeRatePercent", existing.item.feeRatePercent) else existing.item.feeRatePercent,
                        expenseCost = if (obj.has("expenseCost") && !obj.isNull("expenseCost")) obj.optInt("expenseCost", existing.item.expenseCost) else existing.item.expenseCost,
                        useCustomSettings = if (obj.has("useCustomSettings") && !obj.isNull("useCustomSettings")) obj.optBoolean("useCustomSettings", existing.item.useCustomSettings) else existing.item.useCustomSettings,
                        isShared = true,
                        createdAt = resolvedCreatedAt,
                        updatedAt = resolvedUpdatedAt,
                        soldAt = resolvedSoldAt,
                        syncUuid = resolvedUuid,
                        deletedAt = serverDeletedAt,
                        isDirty = false
                    )
                    dao.updateItem(updated)
                } else {
                    val now = System.currentTimeMillis()
                    val resolvedCreatedAt = serverCreatedAt ?: now
                    val resolvedUpdatedAt = if (serverUpdatedAt > 0L) serverUpdatedAt else resolvedCreatedAt
                    val resolvedSoldAt = if (isSoldFamilyStatus(status)) serverSoldAt else null
                    // ID衝突時は id = 0 で自動採番インサートして別レコードとして保持
                    val insertId = if (!isIdCollision && byId == null && serverId > 0) serverId else 0
                    val newItem = com.example.myapplication.data.local.ItemEntity(
                        id = insertId,
                        title = title,
                        description = description,
                        status = status,
                        price = price,
                        soldPrice = soldPrice,
                        shippingCost = if (obj.has("shippingCost") && !obj.isNull("shippingCost")) obj.optInt("shippingCost", 210) else 210,
                        feeRatePercent = if (obj.has("feeRatePercent") && !obj.isNull("feeRatePercent")) obj.optInt("feeRatePercent", 5) else 5,
                        expenseCost = if (obj.has("expenseCost") && !obj.isNull("expenseCost")) obj.optInt("expenseCost", 55) else 55,
                        useCustomSettings = if (obj.has("useCustomSettings") && !obj.isNull("useCustomSettings")) obj.optBoolean("useCustomSettings", false) else false,
                        mergedParentId = resolvedMergedParentId,
                        mergedChildCount = mergedChildCount,
                        mergedChildTitles = mergedChildTitles,
                        originalTotalBundlePrice = originalTotalBundlePrice,
                        discountAmount = discountAmount,
                        bundleLevel = bundleLevel,
                        isShared = true,
                        createdAt = resolvedCreatedAt,
                        updatedAt = resolvedUpdatedAt,
                        soldAt = resolvedSoldAt,
                        syncUuid = resolvedUuid,
                        deletedAt = serverDeletedAt,
                        isDirty = false
                    )
                    targetItemId = dao.insertItem(newItem).toInt()
                }

                // 写真情報の同期（空MD5エントリは除外して取り込まない）
                val photosArray = obj.optJSONArray("photos")
                val currentPhotos = dao.getPhotosByItemId(targetItemId)
                val hasUnpushedLocalPhoto = currentPhotos.any {
                    !it.isPcSynced && !it.isOriginalDeleted && it.filePath.isNotBlank() && File(it.filePath).exists()
                }
                val serverHashes = mutableListOf<String>()
                if (photosArray != null) {
                    for (pIdx in 0 until photosArray.length()) {
                        val h = photosArray.getJSONObject(pIdx).optString("md5Hash", "").trim().lowercase()
                        if (h.isNotBlank()) serverHashes.add(h)
                    }
                }
                val localHashes = currentPhotos.map { it.md5Hash.trim().lowercase() }
                val hashListDiffers = serverHashes != localHashes
                val hasInvalidDisplay = currentPhotos.any { it.displayFile == null }

                if (photosArray != null && photosArray.length() == 0) {
                    if (currentPhotos.isNotEmpty() && !hasUnpushedLocalPhoto) {
                        dao.deletePhotosByItemId(targetItemId)
                    }
                } else if (photosArray != null && photosArray.length() > 0) {
                    val shouldUpdatePhotos = (existing == null) ||
                        (!hasUnpushedLocalPhoto && (hashListDiffers || hasInvalidDisplay))

                    if (shouldUpdatePhotos) {
                        val newPhotoEntities = mutableListOf<PhotoEntity>()
                        for (pIdx in 0 until photosArray.length()) {
                            val pObj = photosArray.getJSONObject(pIdx)
                            val md5 = pObj.optString("md5Hash", "").trim()
                            if (md5.isBlank()) {
                                Log.w("PHOTO_INTEGRITY", "mergeServerItemsFromJson: skipped blank-MD5 server photo entry (itemId=$targetItemId)")
                                continue
                            }
                            val childId = pObj.optInt("childItemId", 0)
                            var localFilePath = ""
                            var localThumbPath: String? = null
                            var isOrigMissing = false

                            val matched = (currentPhotos + allExistingPhotos).firstOrNull {
                                it.md5Hash.equals(md5, ignoreCase = true) &&
                                    ((it.filePath.isNotBlank() && File(it.filePath).exists()) ||
                                        (!it.thumbnailPath.isNullOrBlank() && File(it.thumbnailPath).exists()))
                            }
                            if (matched != null) {
                                val origFile = if (matched.filePath.isNotBlank()) File(matched.filePath) else null
                                val thumbFile = if (!matched.thumbnailPath.isNullOrBlank()) File(matched.thumbnailPath) else null
                                val origIsReal = origFile != null && origFile.exists() &&
                                    !matched.filePath.equals(matched.thumbnailPath, ignoreCase = true)
                                if (origIsReal) {
                                    localFilePath = matched.filePath
                                    localThumbPath = matched.thumbnailPath
                                    isOrigMissing = false
                                } else if (thumbFile != null && thumbFile.exists()) {
                                    localFilePath = matched.thumbnailPath ?: ""
                                    localThumbPath = matched.thumbnailPath
                                    isOrigMissing = true
                                } else {
                                    localFilePath = matched.filePath
                                    localThumbPath = matched.thumbnailPath
                                    isOrigMissing = matched.isOriginalDeleted
                                }
                            }

                            if (localFilePath.isBlank() && childId > 0) {
                                val resolvedChildId = serverIdToUuid[childId]?.let { childUuid ->
                                    dao.getItemWithPhotosBySyncUuid(childUuid)?.item?.id
                                } ?: childId
                                val childPhotos = dao.getPhotosByItemId(resolvedChildId)
                                val matchedChildPhoto = childPhotos.firstOrNull { it.md5Hash.equals(md5, ignoreCase = true) }
                                    ?: childPhotos.getOrNull(pIdx)
                                    ?: childPhotos.firstOrNull { cp ->
                                        (cp.filePath.isNotBlank() && File(cp.filePath).exists()) ||
                                            (!cp.thumbnailPath.isNullOrBlank() && File(cp.thumbnailPath).exists())
                                    }

                                if (matchedChildPhoto != null) {
                                    val cOrig = if (matchedChildPhoto.filePath.isNotBlank()) File(matchedChildPhoto.filePath) else null
                                    val cThumb = if (!matchedChildPhoto.thumbnailPath.isNullOrBlank()) File(matchedChildPhoto.thumbnailPath) else null
                                    val cOrigIsReal = cOrig != null && cOrig.exists() &&
                                        !matchedChildPhoto.filePath.equals(matchedChildPhoto.thumbnailPath, ignoreCase = true)
                                    if (cOrigIsReal) {
                                        localFilePath = matchedChildPhoto.filePath
                                        localThumbPath = matchedChildPhoto.thumbnailPath
                                        isOrigMissing = false
                                    } else if (cThumb != null && cThumb.exists()) {
                                        localFilePath = matchedChildPhoto.thumbnailPath ?: ""
                                        localThumbPath = matchedChildPhoto.thumbnailPath
                                        isOrigMissing = true
                                    } else {
                                        localFilePath = matchedChildPhoto.filePath
                                        localThumbPath = matchedChildPhoto.thumbnailPath
                                        isOrigMissing = matchedChildPhoto.isOriginalDeleted
                                    }
                                }
                            }

                            val hasRealOriginal = localFilePath.isNotBlank() &&
                                !localFilePath.equals(localThumbPath, ignoreCase = true) &&
                                File(localFilePath).exists()

                            newPhotoEntities.add(
                                PhotoEntity(
                                    itemId = targetItemId,
                                    filePath = localFilePath,
                                    thumbnailPath = localThumbPath,
                                    isOriginalDeleted = isOrigMissing || (!hasRealOriginal && !localThumbPath.isNullOrBlank()),
                                    isPcSynced = true,
                                    md5Hash = md5,
                                    syncStatus = "SYNCED",
                                    displayOrder = newPhotoEntities.size
                                )
                            )
                        }

                        dao.deletePhotosByItemId(targetItemId)
                        if (newPhotoEntities.isNotEmpty()) {
                            dao.insertPhotos(newPhotoEntities)
                        }
                    }
                }
                savedTargetItemIdForRepair = targetItemId
            }

            if (itemIdToRepairThumbnails == null && savedTargetItemIdForRepair != null) {
                val syncedPhotos = dao.getPhotosByItemId(savedTargetItemIdForRepair!!)
                if (syncedPhotos.any { isPhotoThumbnailDamagedOrMissing(it) }) {
                    itemIdToRepairThumbnails = savedTargetItemIdForRepair
                }
            }

            if (itemIdToRepairThumbnails != null && repairThumbnails && baseUrl.isNotBlank()) {
                try {
                    repairMissingOrCorruptThumbnailsForItem(itemIdToRepairThumbnails!!, baseUrl, token)
                } catch (e: Exception) {
                    if (e is CancellationException) throw e
                    Log.e("PcServerClient", "Thumbnail repair failed for item $itemIdToRepairThumbnails", e)
                }
            }
        }
        true
    }

    /**
     * PC サーバーから MD5 ハッシュ指定で原本写真を 16KB ストリーミングでファイルへ保存（巨大 ByteArray 確保を回避）
     */
    suspend fun downloadPhotoToFileByHash(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken(),
        md5Hash: String,
        targetFile: File,
        verifyMd5: Boolean = true
    ): Boolean = withContext(Dispatchers.IO) {
        val tempFile = File(targetFile.parentFile ?: context.cacheDir, "${targetFile.name}.dl_tmp")
        try {
            val cleanUrl = baseUrl.trim().trimEnd('/')
            val cleanHash = md5Hash.trim().lowercase()
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http") || cleanHash.isBlank()) {
                return@withContext false
            }
            targetFile.parentFile?.mkdirs()
            val url = "$cleanUrl/api/photos/$cleanHash"
            val reqBuilder = Request.Builder()
                .url(url)
                .addHeader("X-Client-Type", "phone")
                .get()
            if (token.isNotBlank()) {
                reqBuilder.addHeader("Authorization", "Bearer $token")
            }
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) {
                    android.util.Log.w("PC_SYNC", "写真取得失敗 ($cleanHash): HTTP ${response.code}")
                    return@withContext false
                }
                val bodyStream = response.body?.byteStream() ?: return@withContext false
                val digest = if (verifyMd5) MessageDigest.getInstance("MD5") else null
                var totalBytes = 0L
                bodyStream.use { input ->
                    FileOutputStream(tempFile).use { output ->
                        val buffer = ByteArray(16 * 1024)
                        var bytesRead: Int
                        while (input.read(buffer).also { bytesRead = it } != -1) {
                            output.write(buffer, 0, bytesRead)
                            digest?.update(buffer, 0, bytesRead)
                            totalBytes += bytesRead
                        }
                    }
                }
                if (totalBytes <= 0L) {
                    tempFile.delete()
                    return@withContext false
                }
                if (verifyMd5 && digest != null) {
                    val actualMd5 = digest.digest().joinToString("") { "%02x".format(it) }.lowercase()
                    if (actualMd5 != cleanHash) {
                        android.util.Log.w("PC_SYNC", "写真MD5不一致のため破棄: expected=$cleanHash actual=$actualMd5")
                        tempFile.delete()
                        return@withContext false
                    }
                }
                if (targetFile.exists()) targetFile.delete()
                if (!tempFile.renameTo(targetFile)) {
                    FileInputStream(tempFile).use { inp ->
                        FileOutputStream(targetFile).use { out -> inp.copyTo(out) }
                    }
                    tempFile.delete()
                }
                targetFile.exists() && targetFile.length() > 0L
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "downloadPhotoToFileByHash 例外: ${e.message}", e)
            tempFile.delete()
            false
        }
    }

    /**
     * PC サーバーから MD5 ハッシュ指定で原本写真バイナリを認証付き取得（GET /api/photos/{md5Hash}）
     */
    suspend fun downloadPhotoByHash(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken(),
        md5Hash: String,
        verifyMd5: Boolean = true
    ): ByteArray? = withContext(Dispatchers.IO) {
        val tempFile = File.createTempFile("dl_mem_", ".jpg", context.cacheDir)
        try {
            val ok = downloadPhotoToFileByHash(baseUrl, token, md5Hash, tempFile, verifyMd5)
            if (ok && tempFile.exists()) tempFile.readBytes() else null
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "downloadPhotoByHash 例外: ${e.message}", e)
            null
        } finally {
            tempFile.delete()
        }
    }

    private fun isPhotoThumbnailDamagedOrMissing(photo: PhotoEntity): Boolean {
        val display = photo.displayFile ?: return photo.md5Hash.isNotBlank()
        if (display.length() <= 0L) return photo.md5Hash.isNotBlank()
        val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(display.absolutePath, opts)
        if (opts.outWidth <= 0 || opts.outHeight <= 0) return photo.md5Hash.isNotBlank()

        // 原本ファイルが現存していてサムネイルと別ファイルである場合のみ原本MD5との整合性もチェック
        if (!photo.isOriginalDeleted && photo.md5Hash.isNotBlank() && photo.filePath.isNotBlank()) {
            val orig = File(photo.filePath)
            if (orig.exists() && !orig.absolutePath.equals(photo.thumbnailPath, ignoreCase = true)) {
                val origMd5 = photoStorageManager.calculateMd5(orig)
                if (origMd5.isNotBlank() && !origMd5.equals(photo.md5Hash, ignoreCase = true)) {
                    return true
                }
            }
        }
        return false
    }

    /**
     * サムネイル欠損または破損ファイルを検知した際、PCサーバーの /api/photos/{md5Hash} から自己修復する
     */
    suspend fun repairMissingOrCorruptThumbnailsForItem(
        itemId: Int,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Int = withContext(Dispatchers.IO) {
        val cleanUrl = baseUrl.trim().trimEnd('/')
        if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext 0

        val dao = repository.getDao()
        val photos = dao.getPhotosByItemId(itemId)
        val settings = repository.defaultSettings.value
        var repairedCount = 0

        for (photo in photos) {
            if (photo.md5Hash.isBlank() || !isPhotoThumbnailDamagedOrMissing(photo)) continue
            val tempOrig = File(context.cacheDir, "repair_${photo.md5Hash.take(16)}.jpg")
            try {
                val downloaded = downloadPhotoToFileByHash(cleanUrl, token, photo.md5Hash, tempOrig, verifyMd5 = true)
                if (!downloaded) continue
                val newThumbPath = photoStorageManager.createThumbnail(
                    sourceFile = tempOrig,
                    maxDim = settings.thumbnailMaxDimension,
                    quality = settings.thumbnailQuality
                ) ?: continue

                val origExists = photo.filePath.isNotBlank() && File(photo.filePath).exists() &&
                    !photo.filePath.equals(photo.thumbnailPath, ignoreCase = true)
                val newFilePath = if (origExists) photo.filePath else newThumbPath
                repository.withDbMutationLock {
                    dao.updatePhotoRepairedThumbnail(
                        photoId = photo.id,
                        thumbPath = newThumbPath,
                        isOriginalDeleted = !origExists,
                        newFilePath = newFilePath
                    )
                }
                repairedCount++
            } finally {
                if (tempOrig.exists()) tempOrig.delete()
            }
        }
        repairedCount
    }

}
