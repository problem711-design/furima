package com.example.myapplication.data.server

import android.content.Context
import android.graphics.BitmapFactory
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.PhotoStorageManager
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.FeePreset
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.local.TextTemplate
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.File
import java.util.concurrent.TimeUnit

data class PcSyncResult(
    val isSuccess: Boolean,
    val errorMessage: String? = null,
    val syncedItemsCount: Int = 0,
    val syncedPhotosCount: Int = 0,
    val freedMb: Int = 0,
    val missingOriginalPhotosCount: Int = 0,
    val storeId: String? = null
)

class PcServerClient(
    private val context: Context,
    private val repository: FleaMarketRepository
) {
    private val photoStorageManager = PhotoStorageManager(context)
    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .build()

    private val prefs = context.getSharedPreferences("pc_sync_prefs", Context.MODE_PRIVATE)

    private val moshi = Moshi.Builder()
        .addLast(KotlinJsonAdapterFactory())
        .build()

    private val syncItemsRequestAdapter = moshi.adapter(SyncItemsRequest::class.java)
    private val syncItemsResponseAdapter = moshi.adapter(SyncItemsResponse::class.java)
    private val syncPhotoResponseAdapter = moshi.adapter(SyncPhotoResponse::class.java)
    private val syncSettingsAdapter = moshi.adapter(SyncSettingsDto::class.java)

    private fun toSyncSettingsDto(settings: DefaultSettings): SyncSettingsDto {
        return SyncSettingsDto(
            defaultFeeRatePercent = settings.defaultFeeRatePercent,
            defaultShippingCost = settings.defaultShippingCost,
            defaultExpenseCost = settings.defaultExpenseCost,
            activePresetId = settings.activePresetId,
            presets = settings.presets.map { p ->
                SyncFeePresetDto(
                    id = p.id,
                    name = p.name,
                    feeRatePercent = p.feeRatePercent,
                    shippingCost = p.shippingCost,
                    expenseCost = p.expenseCost
                )
            },
            titleTemplates = settings.titleTemplates.map { t ->
                SyncTextTemplateDto(id = t.id, name = t.name, content = t.content)
            },
            descriptionTemplates = settings.descriptionTemplates.map { t ->
                SyncTextTemplateDto(id = t.id, name = t.name, content = t.content)
            },
            settingsUpdatedAt = settings.settingsUpdatedAt
        )
    }

    private fun applyServerSettingsDto(dto: SyncSettingsDto, base: DefaultSettings): DefaultSettings {
        val mergedPresets = dto.presets.map { p ->
            FeePreset(
                id = p.id,
                name = p.name,
                feeRatePercent = p.feeRatePercent.coerceAtLeast(0),
                shippingCost = p.shippingCost.coerceAtLeast(0),
                expenseCost = p.expenseCost.coerceAtLeast(0)
            )
        }.ifEmpty { base.presets }
        val resolvedActiveId = if (mergedPresets.any { it.id == dto.activePresetId }) {
            dto.activePresetId
        } else {
            mergedPresets.firstOrNull()?.id ?: base.activePresetId
        }
        return base.copy(
            defaultFeeRatePercent = dto.defaultFeeRatePercent.coerceAtLeast(0),
            defaultShippingCost = dto.defaultShippingCost.coerceAtLeast(0),
            defaultExpenseCost = dto.defaultExpenseCost.coerceAtLeast(0),
            activePresetId = resolvedActiveId,
            presets = mergedPresets,
            titleTemplates = dto.titleTemplates.map { t ->
                TextTemplate(id = t.id, name = t.name, content = t.content)
            },
            descriptionTemplates = dto.descriptionTemplates.map { t ->
                TextTemplate(id = t.id, name = t.name, content = t.content)
            },
            settingsUpdatedAt = dto.settingsUpdatedAt
        )
    }

    suspend fun pushSettingsToServer(baseUrl: String = getSavedServerUrl(), token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val currentSettings = repository.defaultSettings.value
            val jsonStr = syncSettingsAdapter.toJson(toSyncSettingsDto(currentSettings))
            val body = jsonStr.toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/settings").put(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string()
                if (!respStr.isNullOrBlank()) {
                    val serverDto = syncSettingsAdapter.fromJson(respStr)
                    if (serverDto != null && serverDto.settingsUpdatedAt > 0L) {
                        repository.updateDefaultSettings(
                            applyServerSettingsDto(serverDto, repository.defaultSettings.value),
                            stampNow = false
                        )
                    }
                }
                true
            }
        } catch (e: Exception) {
            android.util.Log.e("PC_SYNC", "pushSettingsToServer 例外: ${e.message}", e)
            false
        }
    }

    suspend fun fetchAndSyncServerSettings(baseUrl: String = getSavedServerUrl(), token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val reqBuilder = Request.Builder().url("$cleanUrl/api/settings").get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string() ?: return@withContext false
                val serverDto = syncSettingsAdapter.fromJson(respStr) ?: return@withContext false
                val localSettings = repository.defaultSettings.value
                when {
                    serverDto.settingsUpdatedAt > localSettings.settingsUpdatedAt -> {
                        repository.updateDefaultSettings(
                            applyServerSettingsDto(serverDto, localSettings),
                            stampNow = false
                        )
                    }
                    localSettings.settingsUpdatedAt > serverDto.settingsUpdatedAt -> {
                        pushSettingsToServer(cleanUrl, token)
                    }
                }
                true
            }
        } catch (e: Exception) {
            android.util.Log.e("PC_SYNC", "fetchAndSyncServerSettings 例外: ${e.message}", e)
            false
        }
    }

    fun getSavedStoreId(): String {
        return prefs.getString("pc_store_id", "") ?: ""
    }

    fun saveStoreId(storeId: String) {
        prefs.edit().putString("pc_store_id", storeId).apply()
    }

    fun getSavedAuthToken(): String {
        return prefs.getString("pc_auth_token", "") ?: ""
    }

    fun saveAuthToken(token: String) {
        prefs.edit().putString("pc_auth_token", token).apply()
    }

    fun getSavedServerUrl(): String {
        return prefs.getString("pc_server_url", "") ?: ""
    }

    fun saveServerUrl(url: String) {
        prefs.edit().putString("pc_server_url", url).apply()
    }

    suspend fun checkServerStatus(baseUrl: String, token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val url = "$cleanUrl/api/server/status"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            false
        }
    }

    suspend fun sendHeartbeatWithRevision(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): HeartbeatInfo = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext HeartbeatInfo(false)
            val url = "$cleanUrl/api/server/heartbeat"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext HeartbeatInfo(false)
                val bodyStr = response.body?.string() ?: ""
                if (bodyStr.isNotBlank()) {
                    try {
                        val json = JSONObject(bodyStr)
                        return@withContext HeartbeatInfo(
                            isConnected = true,
                            dataRevision = json.optLong("dataRevision", 0L),
                            settingsRevision = json.optLong("settingsRevision", 0L)
                        )
                    } catch (_: Exception) {
                    }
                }
                HeartbeatInfo(isConnected = true)
            }
        } catch (_: Exception) {
            HeartbeatInfo(false)
        }
    }

    suspend fun sendHeartbeat(baseUrl: String, token: String = getSavedAuthToken()): Boolean {
        return sendHeartbeatWithRevision(baseUrl, token).isConnected
    }

    suspend fun fetchPermanentlyDeletedUuids(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): Set<String> = withContext(Dispatchers.IO) {
        val result = mutableSetOf<String>()
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext result
            val url = "$cleanUrl/api/server/status"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (response.isSuccessful) {
                    val bodyStr = response.body?.string() ?: ""
                    if (bodyStr.isNotBlank()) {
                        val json = JSONObject(bodyStr)
                        val arr = json.optJSONArray("permanentlyDeletedUuids")
                        if (arr != null) {
                            for (i in 0 until arr.length()) {
                                val u = arr.optString(i, "").trim()
                                if (u.isNotBlank()) result.add(u)
                            }
                        }
                    }
                }
            }
        } catch (_: Exception) {
        }
        result
    }

    suspend fun fetchServerHashes(baseUrl: String, token: String = getSavedAuthToken()): Set<String> = withContext(Dispatchers.IO) {
        val hashSet = mutableSetOf<String>()
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext hashSet
            val url = "$cleanUrl/api/sync/hashes"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (response.isSuccessful) {
                    val bodyStr = response.body?.string() ?: ""
                    val json = JSONObject(bodyStr)
                    val array = json.optJSONArray("hashes")
                    if (array != null) {
                        for (i in 0 until array.length()) {
                            hashSet.add(array.getString(i))
                        }
                    }
                }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
        hashSet
    }

    /**
     * プロトコル v4 syncUuid / deletedAt / tombstone / isDirty 保護付き差分双方向同期
     */
    suspend fun syncDeltaToPcServer(baseUrl: String, token: String = getSavedAuthToken()): PcSyncResult = withContext(Dispatchers.IO) {
        var syncedPhotoCount = 0
        var freedBytes = 0L

        val cleanUrl = baseUrl.trimEnd('/')
        if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) {
            return@withContext PcSyncResult(
                isSuccess = false,
                errorMessage = "PCサーバーURLが正しく設定されていません ($baseUrl)"
            )
        }

        try {
            android.util.Log.i("PC_SYNC", "=== プロトコル v4 同期開始 ($cleanUrl) ===")
            val savedStoreId = getSavedStoreId()
            val dao = repository.getDao()

            // まとめ子商品・ゴミ箱内商品を含む全商品を取得して状態伝播する
            val itemsWithPhotos = dao.getAllItemsWithPhotosForSync()
            val pushedUpdatedAtById = itemsWithPhotos.associate { it.item.id to it.item.updatedAt }
            val idToUuidMap = itemsWithPhotos.associate { it.item.id to it.item.syncUuid }

            val dtoList = mutableListOf<SyncItemDto>()
            val activeFolderNames = mutableListOf<String>()
            val photoFileMap = mutableMapOf<Int, Pair<File, String>>()

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
                    var md5 = photo.md5Hash
                    val file = File(photo.filePath)
                    if (md5.isBlank() && file.exists()) {
                        md5 = photoStorageManager.calculateMd5(file)
                        if (md5.isNotBlank()) {
                            dao.updatePhotoMd5Hash(photo.id, md5)
                        }
                    }

                    photoDtoList.add(SyncPhotoDto(id = photo.id, md5Hash = md5))

                    if (file.exists() && md5.isNotBlank()) {
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
                            item.status == "SOLD" -> "売却済み"
                            item.status == "MERGED" -> "まとめ売却"
                            item.status == "DRAFT" -> "未出品"
                            else -> "出品中"
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

            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) {
                    return@withContext PcSyncResult(
                        isSuccess = false,
                        errorMessage = "サーバーエラー応答 (POST /api/sync/items): HTTP ${response.code}"
                    )
                }

                val respStr = response.body?.string() ?: ""
                val respDto = syncItemsResponseAdapter.fromJson(respStr)
                    ?: return@withContext PcSyncResult(
                        isSuccess = false,
                        errorMessage = "サーバー応答のパースに失敗しました"
                    )

                respStoreId = respDto.storeId ?: ""

                // 応答 storeId の一致検証
                if (savedStoreId.isNotBlank() && respStoreId.isNotBlank() && !savedStoreId.equals(respStoreId, ignoreCase = true)) {
                    return@withContext PcSyncResult(
                        isSuccess = false,
                        errorMessage = "別の保存先です (Store ID不一致)"
                    )
                }

                if (savedStoreId.isBlank() && respStoreId.isNotBlank()) {
                    saveStoreId(respStoreId)
                }

                syncedHashesSet.addAll(respDto.syncedHashes)
                neededHashesSet.addAll(respDto.neededHashes)
                serverDeletedUuids.addAll(respDto.permanentlyDeletedUuids.map { it.trim() }.filter { it.isNotBlank() })
            }

            // PCへ正常に伝達済みの完全削除UUIDをローカル待機リストから除去
            if (pendingDeletedUuids.isNotEmpty()) {
                repository.removePendingDeletedUuids(pendingDeletedUuids)
            }

            // 送信時点と updatedAt が一致する商品の未同期フラグを解除
            for ((itemId, pushedUpdatedAt) in pushedUpdatedAtById) {
                dao.markItemSyncedIfUnchanged(itemId, pushedUpdatedAt)
            }

            // サーバー側から通知された完全削除済みUUIDがあればローカルからも物理削除
            if (serverDeletedUuids.isNotEmpty()) {
                for (delUuid in serverDeletedUuids) {
                    val localTarget = dao.getItemWithPhotosBySyncUuid(delUuid)
                    if (localTarget != null && !localTarget.item.isDirty) {
                        repository.permanentlyDeleteItem(localTarget, recordTombstoneForSync = false)
                    }
                }
            }

            // 2. neededHashes のうち、元ファイルがあるものを POST /api/sync/photo で個別送信
            val newlyStoredHashes = mutableSetOf<String>()

            for ((_, pair) in photoFileMap) {
                val file = pair.first
                val md5 = pair.second

                if (neededHashesSet.contains(md5) && file.exists()) {
                    val photoBytes = file.readBytes()
                    val photoUrl = "$cleanUrl/api/sync/photo"
                    val photoBody = photoBytes.toRequestBody("application/octet-stream".toMediaType())

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
                            }
                        } else {
                            android.util.Log.w("PC_SYNC", "写真送信失敗 (MD5 $md5): HTTP ${photoResp.code}")
                        }
                    }
                }
            }

            val allConfirmedHashes = syncedHashesSet + newlyStoredHashes

            // 3. 高画質削除判定 (条件 storeId一致 & サーバー保存確認済み & サムネイル存在)
            val currentStoreId = getSavedStoreId()
            val isStoreIdValid = currentStoreId.isNotBlank() && currentStoreId.equals(respStoreId, ignoreCase = true)

            var missingOriginalCount = 0
            val deletedOrigPaths = mutableSetOf<String>()

            for (itemWithPhotos in itemsWithPhotos) {
                if (itemWithPhotos.item.deletedAt != null) continue
                for (photo in itemWithPhotos.photos) {
                    var md5 = photo.md5Hash
                    if (md5.isBlank()) {
                        val f = File(photo.filePath)
                        if (f.exists()) md5 = photoStorageManager.calculateMd5(f)
                    }

                    val isConfirmedOnServer = md5.isNotBlank() && allConfirmedHashes.contains(md5.lowercase())

                    if (isConfirmedOnServer) {
                        dao.updatePhotoPcSynced(photo.id, true)
                    }

                    if (photo.isOriginalDeleted) {
                        if (md5.isNotBlank() && !isConfirmedOnServer) {
                            missingOriginalCount++
                        }
                    }

                    if (isStoreIdValid && isConfirmedOnServer) {
                        if (!photo.isOriginalDeleted) {
                            val file = File(photo.filePath)
                            val hasThumb = photo.thumbnailPath != null &&
                                           !photo.thumbnailPath.equals(photo.filePath, ignoreCase = true) &&
                                           File(photo.thumbnailPath).exists()
                            if (hasThumb && deletedOrigPaths.contains(photo.filePath)) {
                                dao.markOriginalDeletedByFilePath(photo.filePath, photo.thumbnailPath!!)
                            } else if (file.exists() && hasThumb) {
                                val fileSize = file.length()
                                if (file.delete()) {
                                    deletedOrigPaths.add(photo.filePath)
                                    freedBytes += fileSize
                                    syncedPhotoCount++
                                    dao.markOriginalDeletedByFilePath(photo.filePath, photo.thumbnailPath!!)
                                }
                            }
                        }
                    }
                }
            }

            return@withContext PcSyncResult(
                isSuccess = true,
                syncedItemsCount = itemsWithPhotos.size,
                syncedPhotosCount = syncedPhotoCount,
                freedMb = (freedBytes / (1024 * 1024)).toInt(),
                missingOriginalPhotosCount = missingOriginalCount,
                storeId = respStoreId
            )
        } catch (e: Exception) {
            android.util.Log.e("PC_SYNC", "プロトコル v4 同期例外: ${e.message}", e)
            return@withContext PcSyncResult(
                isSuccess = false,
                errorMessage = e.localizedMessage ?: e.message ?: "通信エラーが発生しました"
            )
        }
    }

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
            android.util.Log.e("PC_SYNC", "fetchServerItems 例外: ${e.message}", e)
            false
        }
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

        // 1. サーバー側で完全削除されたUUIDをローカルからも物理削除（未送信ローカル変更がない場合）
        if (permanentlyDeletedUuids.isNotEmpty()) {
            for (delUuid in permanentlyDeletedUuids) {
                val cleanUuid = delUuid.trim()
                if (cleanUuid.isBlank()) continue
                val existingByUuid = dao.getItemWithPhotosBySyncUuid(cleanUuid)
                if (existingByUuid != null && !existingByUuid.item.isDirty) {
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

            val byUuid = if (serverSyncUuid.isNotBlank()) dao.getItemWithPhotosBySyncUuid(serverSyncUuid) else null
            val byId = if (serverId > 0) dao.getItemWithPhotosById(serverId) else null
            val isIdCollision = (byUuid == null && byId != null && serverSyncUuid.isNotBlank() &&
                byId.item.syncUuid.isNotBlank() && !byId.item.syncUuid.equals(serverSyncUuid, ignoreCase = true))

            val existing = byUuid ?: if (!isIdCollision) byId else null

            val serverUpdatedAt = if (obj.has("updatedAt") && !obj.isNull("updatedAt")) obj.optLong("updatedAt", 0L) else 0L

            // isDirty 保護：ローカルに未送信の変更があり、ローカル updatedAt がサーバーより新しい／同等なら上書きしない
            if (existing != null && existing.item.isDirty &&
                (serverUpdatedAt <= 0L || existing.item.updatedAt >= serverUpdatedAt)
            ) {
                continue
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
                val resolvedSoldAt = if (status == "SOLD") {
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
                val resolvedSoldAt = if (status == "SOLD") serverSoldAt else null
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

            // 写真情報の同期
            val photosArray = obj.optJSONArray("photos")
            val currentPhotos = dao.getPhotosByItemId(targetItemId)
            val hasUnpushedLocalPhoto = currentPhotos.any {
                !it.isPcSynced && !it.isOriginalDeleted && it.filePath.isNotBlank() && File(it.filePath).exists()
            }
            val serverHashes = mutableListOf<String>()
            if (photosArray != null) {
                for (pIdx in 0 until photosArray.length()) {
                    serverHashes.add(photosArray.getJSONObject(pIdx).optString("md5Hash", "").trim().lowercase())
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
                        val childId = pObj.optInt("childItemId", 0)
                        var localFilePath = ""
                        var localThumbPath: String? = null
                        var isOrigMissing = false

                        if (md5.isNotBlank()) {
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
                        }

                        if (localFilePath.isBlank() && childId > 0) {
                            val resolvedChildId = serverIdToUuid[childId]?.let { childUuid ->
                                dao.getItemWithPhotosBySyncUuid(childUuid)?.item?.id
                            } ?: childId
                            val childPhotos = dao.getPhotosByItemId(resolvedChildId)
                            val matchedChildPhoto = (if (md5.isNotBlank()) {
                                childPhotos.firstOrNull { it.md5Hash.equals(md5, ignoreCase = true) }
                            } else null)
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
                                displayOrder = pIdx
                            )
                        )
                    }

                    if (newPhotoEntities.isNotEmpty()) {
                        dao.deletePhotosByItemId(targetItemId)
                        dao.insertPhotos(newPhotoEntities)
                    }
                }
            }

            if (repairThumbnails && baseUrl.isNotBlank()) {
                val syncedPhotos = dao.getPhotosByItemId(targetItemId)
                if (syncedPhotos.any { isPhotoThumbnailDamagedOrMissing(it) }) {
                    repairMissingOrCorruptThumbnailsForItem(targetItemId, baseUrl, token)
                }
            }
        }
        true
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
        try {
            val cleanUrl = baseUrl.trim().trimEnd('/')
            val cleanHash = md5Hash.trim().lowercase()
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http") || cleanHash.isBlank()) {
                return@withContext null
            }
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
                    return@withContext null
                }
                val bytes = response.body?.bytes() ?: return@withContext null
                if (bytes.isEmpty()) return@withContext null

                if (verifyMd5) {
                    val actualMd5 = photoStorageManager.calculateMd5(bytes).lowercase()
                    if (actualMd5 != cleanHash) {
                        android.util.Log.w("PC_SYNC", "写真MD5不一致のため破棄: expected=$cleanHash actual=$actualMd5")
                        return@withContext null
                    }
                }
                bytes
            }
        } catch (e: Exception) {
            android.util.Log.e("PC_SYNC", "downloadPhotoByHash 例外: ${e.message}", e)
            null
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
            val bytes = downloadPhotoByHash(cleanUrl, token, photo.md5Hash, verifyMd5 = true) ?: continue
            val newThumbPath = photoStorageManager.createThumbnailFromBytes(
                bytes = bytes,
                fileNameHint = "${photo.md5Hash.take(16)}.jpg",
                maxDim = settings.thumbnailMaxDimension,
                quality = settings.thumbnailQuality
            ) ?: continue

            val origExists = photo.filePath.isNotBlank() && File(photo.filePath).exists() &&
                !photo.filePath.equals(photo.thumbnailPath, ignoreCase = true)
            val newFilePath = if (origExists) photo.filePath else newThumbPath
            dao.updatePhotoRepairedThumbnail(
                photoId = photo.id,
                thumbPath = newThumbPath,
                isOriginalDeleted = !origExists,
                newFilePath = newFilePath
            )
            repairedCount++
        }
        repairedCount
    }

    /**
     * PC サーバーへまとめ買い解除（POST /api/items/{parentId}/unmerge）を通知
     */
    suspend fun unmergeOnPcServer(parentId: Int): Boolean = withContext(Dispatchers.IO) {
        val cleanUrl = getSavedServerUrl().trim().removeSuffix("/")
        if (cleanUrl.isBlank()) return@withContext false
        val token = getSavedAuthToken()
        val url = "$cleanUrl/api/items/$parentId/unmerge"
        val reqBuilder = Request.Builder().url(url).post(ByteArray(0).toRequestBody(null))
        if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
        try {
            client.newCall(reqBuilder.build()).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            android.util.Log.e("PC_SYNC", "unmergeOnPcServer 例外: ${e.message}", e)
            false
        }
    }
}

