package com.example.myapplication.data.server

import android.util.Log

import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.PhotoStorageManager
import com.example.myapplication.data.local.isSoldFamilyStatus
import com.example.myapplication.data.local.mergedChildStatusLabelOf
import com.example.myapplication.data.local.statusLabelOf
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okio.BufferedSink
import java.io.File
import java.io.FileInputStream

private class StreamingFileRequestBody(
    private val file: File,
    private val mediaType: MediaType,
    private val onProgressBytes: (Long) -> Unit
) : RequestBody() {
    override fun contentType(): MediaType = mediaType
    override fun contentLength(): Long = file.length()
    override fun writeTo(sink: BufferedSink) {
        val buffer = ByteArray(16 * 1024)
        var sentThisFile = 0L
        var lastReportedBytes = 0L
        FileInputStream(file).use { input ->
            var read: Int
            while (input.read(buffer).also { read = it } != -1) {
                sink.write(buffer, 0, read)
                sentThisFile += read
                if (sentThisFile - lastReportedBytes >= 64 * 1024L) {
                    lastReportedBytes = sentThisFile
                    onProgressBytes(sentThisFile)
                }
            }
        }
        if (sentThisFile != lastReportedBytes) {
            onProgressBytes(sentThisFile)
        }
    }
}

internal class PcDeltaSyncUploader(
    private val transport: PcServerTransport,
    private val repository: FleaMarketRepository,
    private val photoStorageManager: PhotoStorageManager
) {
    private val client get() = transport.client
    private val syncItemsRequestAdapter get() = transport.syncItemsRequestAdapter
    private val syncItemsResponseAdapter get() = transport.syncItemsResponseAdapter
    private val syncPhotoResponseAdapter get() = transport.syncPhotoResponseAdapter

    private fun getSavedStoreId(): String = transport.getSavedStoreId()
    private fun saveStoreId(storeId: String) = transport.saveStoreId(storeId)
    private fun getSavedServerUrl(): String = transport.getSavedServerUrl()
    private fun getSavedAuthToken(): String = transport.getSavedAuthToken()
    private fun toSyncSettingsDto(settings: com.example.myapplication.data.local.DefaultSettings) =
        transport.toSyncSettingsDto(settings)
    private fun applyServerSettingsDto(
        dto: SyncSettingsDto,
        base: com.example.myapplication.data.local.DefaultSettings
    ) = transport.applyServerSettingsDto(dto, base)
    private suspend fun fetchServerHashes(baseUrl: String, token: String): Set<String> =
        transport.fetchServerHashes(baseUrl, token)

    /**
     * プロトコル v4 syncUuid / deletedAt / tombstone / isDirty 保護付き差分双方向同期
     */
    suspend fun syncDeltaToPcServer(
        baseUrl: String,
        token: String = getSavedAuthToken(),
        onProgress: (PcSyncProgress) -> Unit = {}
    ): PcSyncResult = withContext(Dispatchers.IO) {
        var syncedPhotoCount = 0
        var freedBytes = 0L

        val cleanUrl = baseUrl.trimEnd('/')
        if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) {
            val msg = "PCサーバーURLが正しく設定されていません ($baseUrl)"
            onProgress(PcSyncProgress(phase = SyncPhase.ERROR, detailMessage = "URL未設定", errorDetail = msg))
            return@withContext PcSyncResult(
                isSuccess = false,
                errorMessage = msg
            )
        }

        try {
            android.util.Log.i("PC_SYNC", "=== プロトコル v4 同期開始 ($cleanUrl) ===")
            onProgress(
                PcSyncProgress(
                    phase = SyncPhase.SYNCING_ITEMS,
                    progressPercent = 5,
                    detailMessage = "商品メタデータを準備中..."
                )
            )
            val savedStoreId = getSavedStoreId()
            val dao = repository.getDao()
            repository.cleanupGhostBlankMd5Photos()
            // §83-4: 浄化で壊れ写真行を除去した場合は、同期進捗メッセージで UI へ通知する
            val (cleanedGhostCount, cleanedUnrecoverableCount) = repository.consumeGhostCleanupSummary()
            if (cleanedGhostCount > 0 || cleanedUnrecoverableCount > 0) {
                onProgress(
                    PcSyncProgress(
                        phase = SyncPhase.SYNCING_ITEMS,
                        progressPercent = 6,
                        detailMessage = "壊れた写真参照を除去しました (完全ゴースト ${cleanedGhostCount}件・復旧不能 ${cleanedUnrecoverableCount}件)"
                    )
                )
            }

            // まとめ子商品・ゴミ箱内商品を含む全商品を取得して状態伝播する
            val itemsWithPhotos = dao.getAllItemsWithPhotosForSync()
            val pushedUpdatedAtById = itemsWithPhotos.associate { it.item.id to it.item.updatedAt }
            val idToUuidMap = itemsWithPhotos.associate { it.item.id to it.item.syncUuid }
            val statusByIdMap = itemsWithPhotos.associate { it.item.id to it.item.status }

            val dtoList = mutableListOf<SyncItemDto>()
            val activeFolderNames = mutableListOf<String>()
            val photoFileMap = mutableMapOf<Int, Pair<File, String>>()
            val expectedHashesByItemId = mutableMapOf<Int, MutableSet<String>>()
            val hasUnresolvedLocalPhotoByItemId = mutableMapOf<Int, Boolean>()

            for (itemWithPhotos in itemsWithPhotos) {
                val item = itemWithPhotos.item
                val photos = itemWithPhotos.photos

                val safeTitle = item.title.trim().replace(Regex("[\\\\/:*?\"<>|]"), "_")
                val itemFolderName = "${item.id}_$safeTitle"
                if (!activeFolderNames.contains(itemFolderName)) {
                    activeFolderNames.add(itemFolderName)
                }

                val photoDtoList = mutableListOf<SyncPhotoDto>()
                for (photo in photos) {
                    var md5 = photo.md5Hash.trim().lowercase()
                    val file = File(photo.filePath)
                    if (md5.isBlank() && file.exists() && file.isFile && file.length() > 0L) {
                        val calculated = photoStorageManager.calculateMd5(file).trim().lowercase()
                        if (calculated.isNotBlank()) {
                            md5 = calculated
                            repository.withDbMutationLock {
                                dao.updatePhotoMd5Hash(photo.id, md5)
                            }
                        }
                    }

                    if (md5.isBlank()) {
                        Log.w(
                            "PHOTO_INTEGRITY",
                            "syncDeltaToPcServer: skipping blank-MD5 photo (itemId=${item.id}, photoId=${photo.id}, path=${photo.filePath})"
                        )
                        hasUnresolvedLocalPhotoByItemId[item.id] = true
                        continue
                    }

                    expectedHashesByItemId.getOrPut(item.id) { mutableSetOf() }.add(md5)
                    photoDtoList.add(SyncPhotoDto(id = photo.id, md5Hash = md5))

                    if (file.exists() && file.isFile && file.length() > 0L) {
                        photoFileMap[photo.id] = Pair(file, md5)
                    }
                }

                dtoList.add(
                    SyncItemDto(
                        id = item.id,
                        syncUuid = item.syncUuid,
                        deletedAt = item.deletedAt,
                        title = item.title,
                        description = item.description,
                        status = item.status,
                        price = item.price,
                        soldPrice = item.soldPrice,
                        profit = item.getCalculatedProfit(repository.defaultSettings.value),
                        statusLabel = when {
                            item.deletedAt != null -> "ゴミ箱"
                            item.status == "MERGED" -> mergedChildStatusLabelOf(
                                item.mergedParentId?.let { statusByIdMap[it] } ?: "LISTED"
                            )
                            else -> statusLabelOf(item.status)
                        },
                        photos = photoDtoList,
                        mergedParentId = item.mergedParentId,
                        mergedParentSyncUuid = item.mergedParentId?.let { idToUuidMap[it] },
                        mergedChildCount = item.mergedChildCount,
                        bundleLevel = item.bundleLevel,
                        mergedChildTitles = item.mergedChildTitles,
                        originalTotalBundlePrice = item.originalTotalBundlePrice,
                        discountAmount = item.discountAmount,
                        shippingCost = item.shippingCost,
                        feeRatePercent = item.feeRatePercent,
                        expenseCost = item.expenseCost,
                        useCustomSettings = item.useCustomSettings,
                        createdAt = item.createdAt,
                        updatedAt = item.updatedAt,
                        soldAt = item.soldAt
                    )
                )
            }

            // 1. POST /api/sync/items (設定・プリセット・テンプレート・完全削除UUIDも同梱送信)
            val currentSettings = repository.defaultSettings.value
            val settingsDto = toSyncSettingsDto(currentSettings)
            val pendingDeletedUuids = repository.getPendingDeletedUuids()
            // レビュー 24-7: 送信に含める未Push写真削除の記録 (成功時にこの分だけ消去する)
            val pendingPhotoDeletions = repository.getAllPendingPhotoDeletions()
            val syncReq = SyncItemsRequest(
                protocolVersion = 4,
                storeId = savedStoreId,
                items = dtoList,
                activeFolderNames = activeFolderNames,
                defaultSettings = settingsDto,
                permanentlyDeletedUuids = pendingDeletedUuids.toList()
            )

            val reqJsonStr = syncItemsRequestAdapter.toJson(syncReq)
            val itemsUrl = "$cleanUrl/api/sync/items"
            val body = reqJsonStr.toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url(itemsUrl).post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")

            val respStoreId: String
            val syncedHashesSet = mutableSetOf<String>()
            val neededHashesSet = mutableSetOf<String>()
            val serverDeletedUuids = mutableSetOf<String>()

            onProgress(
                PcSyncProgress(
                    phase = SyncPhase.SYNCING_ITEMS,
                    progressPercent = 10,
                    detailMessage = "商品データ (${dtoList.size}件) をPCへ送信中..."
                )
            )

            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) {
                    val errMsg = "サーバーエラー応答 (POST /api/sync/items): HTTP ${response.code}"
                    onProgress(PcSyncProgress(phase = SyncPhase.ERROR, detailMessage = "メタデータ送信失敗", errorDetail = errMsg))
                    return@withContext PcSyncResult(
                        isSuccess = false,
                        errorMessage = errMsg
                    )
                }

                val respStr = response.body?.string() ?: ""
                val respDto = syncItemsResponseAdapter.fromJson(respStr)
                    ?: run {
                        val errMsg = "サーバー応答のパースに失敗しました"
                        onProgress(PcSyncProgress(phase = SyncPhase.ERROR, detailMessage = "応答パース失敗", errorDetail = errMsg))
                        return@withContext PcSyncResult(
                            isSuccess = false,
                            errorMessage = errMsg
                        )
                    }

                respStoreId = respDto.storeId ?: ""

                // 応答 storeId の一致検証
                if (savedStoreId.isNotBlank() && respStoreId.isNotBlank() && !savedStoreId.equals(respStoreId, ignoreCase = true)) {
                    val errMsg = "別の保存先です (Store ID不一致)"
                    onProgress(PcSyncProgress(phase = SyncPhase.ERROR, detailMessage = "Store ID不一致", errorDetail = errMsg))
                    return@withContext PcSyncResult(
                        isSuccess = false,
                        errorMessage = errMsg
                    )
                }

                if (savedStoreId.isBlank() && respStoreId.isNotBlank()) {
                    saveStoreId(respStoreId)
                }

                syncedHashesSet.addAll(
                    respDto.syncedHashes.map { it.trim().lowercase() }.filter { it.isNotBlank() }
                )
                neededHashesSet.addAll(
                    respDto.neededHashes.map { it.trim().lowercase() }.filter { it.isNotBlank() }
                )
                serverDeletedUuids.addAll(respDto.permanentlyDeletedUuids.map { it.trim() }.filter { it.isNotBlank() })
            }

            // PCへ正常に伝達済みの完全削除UUIDをローカル待機リストから除去
            if (pendingDeletedUuids.isNotEmpty()) {
                repository.removePendingDeletedUuids(pendingDeletedUuids)
            }

            // レビュー 24-7: 商品データの送信で削除がサーバーへ伝わったため、送信に含めた記録を消去する
            if (pendingPhotoDeletions.isNotEmpty()) {
                repository.removePendingPhotoDeletions(pendingPhotoDeletions)
            }

            // サーバー側から通知された完全削除済みUUIDがあればローカルからも物理削除
            if (serverDeletedUuids.isNotEmpty()) {
                for (delUuid in serverDeletedUuids) {
                    val localTarget = dao.getItemWithPhotosBySyncUuid(delUuid)
                    if (localTarget != null &&
                        !localTarget.item.isDirty &&
                        !repository.isItemEditingActive(localTarget.item.id)
                    ) {
                        repository.permanentlyDeleteItem(localTarget, recordTombstoneForSync = false)
                    }
                }
            }

            // 2. neededHashes のうち、元ファイルがあるものを POST /api/sync/photo で個別送信
            val newlyStoredHashes = mutableSetOf<String>()
            // §83-2: Step 2 の送信失敗 (HTTP 失敗・stored != true・応答不正) を集計し、
            // 最終結果へ反映する (旧実装は警告ログのみで成功扱いだった)
            var failedUploadCount = 0
            val failedUploadHashes = mutableSetOf<String>()
            val photosToUpload = photoFileMap.values
                .distinctBy { it.second.lowercase() }
                .filter { (file, md5) -> neededHashesSet.contains(md5) && file.exists() }
            val totalUploadCount = photosToUpload.size
            val totalUploadBytes = photosToUpload.sumOf { it.first.length() }.coerceAtLeast(1L)
            var uploadedBytesSoFar = 0L

            photosToUpload.forEachIndexed { idx, (file, md5) ->
                val photoIndex = idx + 1
                val fileSize = file.length()
                val baseBytesBeforeThisFile = uploadedBytesSoFar
                val basePct = (15 + (baseBytesBeforeThisFile * 75 / totalUploadBytes)).toInt().coerceIn(15, 90)
                onProgress(
                    PcSyncProgress(
                        phase = SyncPhase.UPLOADING_PHOTOS,
                        currentPhotoIndex = photoIndex,
                        totalPhotoCount = totalUploadCount,
                        bytesSent = baseBytesBeforeThisFile,
                        totalBytes = totalUploadBytes,
                        progressPercent = basePct,
                        detailMessage = "写真を送信中 ($photoIndex/$totalUploadCount 枚)"
                    )
                )

                val photoUrl = "$cleanUrl/api/sync/photo"
                val photoBody = StreamingFileRequestBody(
                    file = file,
                    mediaType = "application/octet-stream".toMediaType(),
                    onProgressBytes = { sentInFile ->
                        val currentTotalSent = (baseBytesBeforeThisFile + sentInFile).coerceAtMost(totalUploadBytes)
                        val livePct = (15 + (currentTotalSent * 75 / totalUploadBytes)).toInt().coerceIn(15, 90)
                        onProgress(
                            PcSyncProgress(
                                phase = SyncPhase.UPLOADING_PHOTOS,
                                currentPhotoIndex = photoIndex,
                                totalPhotoCount = totalUploadCount,
                                bytesSent = currentTotalSent,
                                totalBytes = totalUploadBytes,
                                progressPercent = livePct,
                                detailMessage = "写真を送信中 ($photoIndex/$totalUploadCount 枚)"
                            )
                        )
                    }
                )

                val photoReqBuilder = Request.Builder()
                    .url(photoUrl)
                    .addHeader("X-Photo-Hash", md5)
                    .post(photoBody)

                if (token.isNotBlank()) photoReqBuilder.addHeader("Authorization", "Bearer $token")

                client.newCall(photoReqBuilder.build()).execute().use { photoResp ->
                    if (photoResp.isSuccessful) {
                        val photoRespStr = photoResp.body?.string() ?: ""
                        val photoRespDto = syncPhotoResponseAdapter.fromJson(photoRespStr)
                        if (photoRespDto != null && photoRespDto.stored && photoRespDto.hash != null) {
                            newlyStoredHashes.add(photoRespDto.hash.lowercase())
                        } else {
                            // §83-2: HTTP 成功でも stored でなければ未完了として集計する
                            failedUploadCount++
                            failedUploadHashes.add(md5)
                            android.util.Log.w("PC_SYNC", "写真保存未確認 (MD5 $md5): stored=${photoRespDto?.stored}")
                        }
                    } else {
                        failedUploadCount++
                        failedUploadHashes.add(md5)
                        android.util.Log.w("PC_SYNC", "写真送信失敗 (MD5 $md5): HTTP ${photoResp.code}")
                    }
                }

                uploadedBytesSoFar += fileSize
                val donePct = (15 + (uploadedBytesSoFar * 75 / totalUploadBytes)).toInt().coerceIn(15, 90)
                onProgress(
                    PcSyncProgress(
                        phase = SyncPhase.UPLOADING_PHOTOS,
                        currentPhotoIndex = photoIndex,
                        totalPhotoCount = totalUploadCount,
                        bytesSent = uploadedBytesSoFar,
                        totalBytes = totalUploadBytes,
                        progressPercent = donePct,
                        detailMessage = "写真を送信中 ($photoIndex/$totalUploadCount 枚)"
                    )
                )
            }

            val allConfirmedHashes = syncedHashesSet + newlyStoredHashes

            // §83-2: 未完了の商品 (未解決ローカル写真・未確認ハッシュ) を集計する
            val incompleteItemIds = mutableSetOf<Int>()

            // 写真アップロード完了を確認できた商品だけ isDirty を解除する
            for ((itemId, pushedUpdatedAt) in pushedUpdatedAtById) {
                if (hasUnresolvedLocalPhotoByItemId[itemId] == true) {
                    Log.w("PHOTO_INTEGRITY", "syncDeltaToPcServer: keeping isDirty=true for itemId=$itemId (unresolved blank-MD5 photo)")
                    incompleteItemIds.add(itemId)
                    continue
                }
                val expectedHashes = expectedHashesByItemId[itemId].orEmpty()
                if (expectedHashes.all { allConfirmedHashes.contains(it) }) {
                    repository.withDbMutationLock {
                        dao.markItemSyncedIfUnchanged(itemId, pushedUpdatedAt)
                    }
                } else {
                    Log.w("PHOTO_INTEGRITY", "syncDeltaToPcServer: keeping isDirty=true for itemId=$itemId (unconfirmed photos remain)")
                    incompleteItemIds.add(itemId)
                }
            }

            // 3. 高画質削除判定 (条件 storeId一致 & サーバー保存確認済み & サムネイル存在 & 編集中/isDirtyでない)
            val currentStoreId = getSavedStoreId()
            val isStoreIdValid = currentStoreId.isNotBlank() && currentStoreId.equals(respStoreId, ignoreCase = true)

            var missingOriginalCount = 0
            val deletedOrigPaths = mutableSetOf<String>()

            for (itemWithPhotos in itemsWithPhotos) {
                val itemId = itemWithPhotos.item.id
                if (itemWithPhotos.item.deletedAt != null) continue
                val itemEditingAtStart = repository.isItemEditingActive(itemId)
                val latestItemAtStart = repository.withDbMutationLock { dao.getItemWithPhotosById(itemId)?.item }
                val itemProtectedAtStart = itemEditingAtStart ||
                    latestItemAtStart == null ||
                    latestItemAtStart.deletedAt != null ||
                    latestItemAtStart.isDirty

                for (photo in itemWithPhotos.photos) {
                    var md5 = photo.md5Hash.trim().lowercase()
                    if (md5.isBlank()) {
                        val f = File(photo.filePath)
                        if (f.exists() && f.isFile && f.length() > 0L) {
                            md5 = photoStorageManager.calculateMd5(f).trim().lowercase()
                        }
                    }
                    if (md5.isBlank()) continue

                    val isConfirmedOnServer = allConfirmedHashes.contains(md5)
                    if (isConfirmedOnServer) {
                        repository.withDbMutationLock {
                            dao.updatePhotoPcSynced(photo.id, true)
                        }
                    }

                    if (photo.isOriginalDeleted && !isConfirmedOnServer) {
                        missingOriginalCount++
                    }

                    if (!itemProtectedAtStart && isStoreIdValid && isConfirmedOnServer && !photo.isOriginalDeleted) {
                        val thumbPath = photo.thumbnailPath
                        val hasThumb = !thumbPath.isNullOrBlank() &&
                            !thumbPath.equals(photo.filePath, ignoreCase = true) &&
                            File(thumbPath).let { it.exists() && it.isFile && it.length() > 0L }
                        if (hasThumb && thumbPath != null) {
                            val file = File(photo.filePath)
                            repository.withDbMutationLock {
                                if (repository.isItemEditingActive(itemId) ||
                                    repository.isFilePathProtectedByEditingOrDirty(photo.filePath)
                                ) {
                                    return@withDbMutationLock
                                }
                                val freshItem = dao.getItemWithPhotosById(itemId)?.item
                                if (freshItem == null || freshItem.deletedAt != null || freshItem.isDirty) {
                                    return@withDbMutationLock
                                }
                                if (deletedOrigPaths.contains(photo.filePath)) {
                                    dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                                } else if (file.exists()) {
                                    val fileSize = file.length()
                                    if (file.delete()) {
                                        deletedOrigPaths.add(photo.filePath)
                                        freedBytes += fileSize
                                        syncedPhotoCount++
                                        dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // §83-2: Step 2 の失敗・未確認が残る場合は成功と報告しない
            // (SyncCoordinator が SyncPhase.ERROR へ遷移し、最終同期時刻を更新しない)
            if (failedUploadCount > 0 || incompleteItemIds.isNotEmpty()) {
                val reason = "写真送信が未完了です (送信失敗 ${failedUploadCount}件・未確認 ${incompleteItemIds.size}商品)。再同期してください"
                onProgress(
                    PcSyncProgress(
                        phase = SyncPhase.ERROR,
                        currentPhotoIndex = totalUploadCount,
                        totalPhotoCount = totalUploadCount,
                        bytesSent = uploadedBytesSoFar,
                        totalBytes = if (totalUploadCount > 0) totalUploadBytes else 0L,
                        progressPercent = 100,
                        detailMessage = "送信未完了 (${itemsWithPhotos.size}件)",
                        errorDetail = reason
                    )
                )
                return@withContext PcSyncResult(
                    isSuccess = false,
                    errorMessage = reason,
                    syncedItemsCount = itemsWithPhotos.size,
                    syncedPhotosCount = syncedPhotoCount,
                    freedMb = (freedBytes / (1024 * 1024)).toInt(),
                    missingOriginalPhotosCount = missingOriginalCount,
                    storeId = respStoreId
                )
            }

            onProgress(
                PcSyncProgress(
                    phase = SyncPhase.SUCCESS,
                    currentPhotoIndex = totalUploadCount,
                    totalPhotoCount = totalUploadCount,
                    bytesSent = uploadedBytesSoFar,
                    totalBytes = if (totalUploadCount > 0) totalUploadBytes else 0L,
                    progressPercent = 100,
                    detailMessage = "送信完了 (${itemsWithPhotos.size}件)"
                )
            )
            return@withContext PcSyncResult(
                isSuccess = true,
                syncedItemsCount = itemsWithPhotos.size,
                syncedPhotosCount = syncedPhotoCount,
                freedMb = (freedBytes / (1024 * 1024)).toInt(),
                missingOriginalPhotosCount = missingOriginalCount,
                storeId = respStoreId
            )
        } catch (e: CancellationException) {
            // §83-1: キャンセルを通信エラー結果へ変換せず伝播させる
            throw e
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "プロトコル v4 同期例外: ${e.message}", e)
            val errMsg = e.localizedMessage ?: e.message ?: "通信エラーが発生しました"
            onProgress(PcSyncProgress(phase = SyncPhase.ERROR, detailMessage = "同期通信エラー", errorDetail = errMsg))
            return@withContext PcSyncResult(
                isSuccess = false,
                errorMessage = errMsg
            )
        }
    }

}
