package com.example.myapplication.data.server

import android.content.Context
import android.graphics.BitmapFactory
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.PhotoStorageManager
import com.example.myapplication.data.local.PhotoEntity
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

    suspend fun sendHeartbeat(baseUrl: String, token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val url = "$cleanUrl/api/server/heartbeat"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            false
        }
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
     * プロトコル v3: JSON メタデータ送信 (POST /api/sync/items) と生バイト送信 (POST /api/sync/photo) による二段階同期
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
            android.util.Log.i("PC_SYNC", "=== プロトコル v3 同期開始: $cleanUrl ===")
            val savedStoreId = getSavedStoreId()

            // スマホ側の全商品・写真リストを取得（同期対象はまとめ内包中の子商品も含む全商品）
            val itemsWithPhotos = repository.getDao().getAllItemsWithPhotosDirect()
            // PC側の不要フォルダ削除から保護するフォルダ名一覧（まとめ済みMERGED子商品フォルダも含む全商品）
            val allItemsForFolders = repository.getDao().getAllItemsWithPhotosDirect()
            val dtoList = mutableListOf<SyncItemDto>()
            val activeFolderNames = mutableListOf<String>()

            for (itemWithPhotos in allItemsForFolders) {
                val item = itemWithPhotos.item
                val safeTitle = item.title.trim().replace(Regex("[\\\\/:*?\"<>|]"), "_")
                val itemFolderName = "${item.id}_$safeTitle"
                if (!activeFolderNames.contains(itemFolderName)) {
                    activeFolderNames.add(itemFolderName)
                }
            }

            // 写真ID -> (File, MD5) のマッピング
            val photoFileMap = mutableMapOf<Int, Pair<File, String>>()

            for (itemWithPhotos in itemsWithPhotos) {
                val item = itemWithPhotos.item
                val photos = itemWithPhotos.photos

                val safeTitle = item.title.trim().replace(Regex("[\\\\/:*?\"<>|]"), "_")
                val itemFolderName = "${item.id}_$safeTitle"
                activeFolderNames.add(itemFolderName)

                val photoDtoList = mutableListOf<SyncPhotoDto>()

                for (photo in photos) {
                    var md5 = photo.md5Hash
                    val file = File(photo.filePath)
                    if (md5.isBlank() && file.exists()) {
                        md5 = photoStorageManager.calculateMd5(file)
                        if (md5.isNotBlank()) {
                            repository.getDao().updatePhotoMd5Hash(photo.id, md5)
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
                        title = item.title,
                        description = item.description,
                        status = item.status,
                        price = item.price,
                        soldPrice = item.soldPrice,
                        profit = item.getCalculatedProfit(repository.defaultSettings.value),
                        statusLabel = when (item.status) {
                            "SOLD" -> "売却済み"
                            "MERGED" -> "まとめ売却"
                            "DRAFT" -> "未出品"
                            else -> "出品中"
                        },
                        photos = photoDtoList,
                        mergedParentId = item.mergedParentId,
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

            // 1. POST /api/sync/items (基本費用設定・プリセット一覧も同梱送信)
            val currentSettings = repository.defaultSettings.value
            val settingsDto = SyncSettingsDto(
                defaultFeeRatePercent = currentSettings.defaultFeeRatePercent,
                defaultShippingCost = currentSettings.defaultShippingCost,
                defaultExpenseCost = currentSettings.defaultExpenseCost,
                activePresetId = currentSettings.activePresetId,
                presets = currentSettings.presets.map { p ->
                    SyncFeePresetDto(
                        id = p.id,
                        name = p.name,
                        feeRatePercent = p.feeRatePercent,
                        shippingCost = p.shippingCost,
                        expenseCost = p.expenseCost
                    )
                }
            )
            val syncReq = SyncItemsRequest(
                protocolVersion = 3,
                storeId = savedStoreId,
                items = dtoList,
                activeFolderNames = activeFolderNames,
                defaultSettings = settingsDto
            )

            val reqJsonStr = syncItemsRequestAdapter.toJson(syncReq)
            val itemsUrl = "$cleanUrl/api/sync/items"
            val body = reqJsonStr.toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url(itemsUrl).post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")

            val respStoreId: String
            val syncedHashesSet = mutableSetOf<String>()
            val neededHashesSet = mutableSetOf<String>()

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

                // 応答 storeId の一致検証 (記憶された storeId があり、不一致の場合は中止)
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
                            android.util.Log.w("PC_SYNC", "写真送信失敗 (MD5: $md5): HTTP ${photoResp.code}")
                        }
                    }
                }
            }

            // 合体: 既存Synced + 今回新規Saved
            val allConfirmedHashes = syncedHashesSet + newlyStoredHashes

            // 3. 高画質削除判定 (条件1: storeId一致, 条件2: allConfirmedHashesに含まれる, 条件3: サムネイル存在)
            val currentStoreId = getSavedStoreId()
            val isStoreIdValid = currentStoreId.isNotBlank() && currentStoreId.equals(respStoreId, ignoreCase = true)

            var missingOriginalCount = 0

            for (itemWithPhotos in itemsWithPhotos) {
                for (photo in itemWithPhotos.photos) {
                    var md5 = photo.md5Hash
                    if (md5.isBlank()) {
                        val f = File(photo.filePath)
                        if (f.exists()) md5 = photoStorageManager.calculateMd5(f)
                    }

                    val isConfirmedOnServer = md5.isNotBlank() && allConfirmedHashes.contains(md5.lowercase())

                    if (isConfirmedOnServer) {
                        repository.getDao().updatePhotoPcSynced(photo.id, true)
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
                            if (file.exists() && hasThumb) {
                                val fileSize = file.length()
                                if (file.delete()) {
                                    freedBytes += fileSize
                                    syncedPhotoCount++
                                    repository.getDao().updatePhotoOriginalDeleted(photo.id, true, photo.thumbnailPath)
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
            android.util.Log.e("PC_SYNC", "プロトコル v3 同期例外: ${e.message}", e)
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
                val dao = repository.getDao()
                val allExistingPhotos = dao.getAllItemsWithPhotosDirect().flatMap { it.photos }

                for (i in 0 until jsonArray.length()) {
                    val obj = jsonArray.getJSONObject(i)
                    val id = obj.optInt("id", 0)
                    if (id <= 0) continue

                    val isDeleted = obj.optBoolean("isDeleted", false)
                    val existing = dao.getItemWithPhotosById(id)

                    if (isDeleted) {
                        if (existing != null) {
                            // まとめ親商品が削除・解除された場合は、子商品のステータスを出品中に復元し、親の写真レコードのみDB削除（子写真の実ファイルは消さない）
                            if (existing.item.mergedChildCount > 0) {
                                dao.restoreMergedChildren(id)
                                dao.deletePhotosByItemId(id)
                                dao.deleteItem(existing.item)
                            } else {
                                repository.deleteItem(existing)
                            }
                        }
                        continue
                    }

                    val title = obj.optString("title", "商品")
                    val description = obj.optString("description", "")
                    val status = obj.optString("status", "LISTED")
                    val price = obj.optInt("price", 0)
                    val soldPrice = obj.optInt("soldPrice", 0)

                    val mergedParentId = if (obj.has("mergedParentId") && !obj.isNull("mergedParentId")) obj.getInt("mergedParentId") else null
                    val mergedChildCount = obj.optInt("mergedChildCount", 0)
                    val mergedChildTitles = obj.optString("mergedChildTitles", "")
                    val originalTotalBundlePrice = obj.optInt("originalTotalBundlePrice", 0)
                    val discountAmount = obj.optInt("discountAmount", 0)
                    val bundleLevel = obj.optInt("bundleLevel", 0)

                    val serverCreatedAt = if (obj.has("createdAt") && !obj.isNull("createdAt")) obj.optLong("createdAt", 0L).takeIf { it > 0L } else null
                    val serverUpdatedAt = if (obj.has("updatedAt") && !obj.isNull("updatedAt")) obj.optLong("updatedAt", 0L).takeIf { it > 0L } else null
                    val serverSoldAt = if (obj.has("soldAt") && !obj.isNull("soldAt")) obj.optLong("soldAt", 0L).takeIf { it > 0L } else null

                    if (existing != null) {
                        val resolvedCreatedAt = when {
                            serverCreatedAt != null && existing.item.createdAt > 0L -> minOf(serverCreatedAt, existing.item.createdAt)
                            serverCreatedAt != null -> serverCreatedAt
                            else -> existing.item.createdAt
                        }
                        val resolvedUpdatedAt = serverUpdatedAt ?: existing.item.updatedAt
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
                            mergedParentId = mergedParentId,
                            mergedChildCount = mergedChildCount,
                            mergedChildTitles = if (mergedChildTitles.isNotBlank()) mergedChildTitles else existing.item.mergedChildTitles,
                            originalTotalBundlePrice = if (originalTotalBundlePrice > 0) originalTotalBundlePrice else existing.item.originalTotalBundlePrice,
                            discountAmount = if (discountAmount > 0) discountAmount else existing.item.discountAmount,
                            bundleLevel = if (bundleLevel > 0) bundleLevel else existing.item.bundleLevel,
                            shippingCost = if (obj.has("shippingCost") && !obj.isNull("shippingCost")) obj.optInt("shippingCost", existing.item.shippingCost) else existing.item.shippingCost,
                            feeRatePercent = if (obj.has("feeRatePercent") && !obj.isNull("feeRatePercent")) obj.optInt("feeRatePercent", existing.item.feeRatePercent) else existing.item.feeRatePercent,
                            expenseCost = if (obj.has("expenseCost") && !obj.isNull("expenseCost")) obj.optInt("expenseCost", existing.item.expenseCost) else existing.item.expenseCost,
                            useCustomSettings = if (obj.has("useCustomSettings") && !obj.isNull("useCustomSettings")) obj.optBoolean("useCustomSettings", existing.item.useCustomSettings) else existing.item.useCustomSettings,
                            createdAt = resolvedCreatedAt,
                            updatedAt = resolvedUpdatedAt,
                            soldAt = resolvedSoldAt
                        )
                        dao.updateItem(updated)
                    } else {
                        val now = System.currentTimeMillis()
                        val resolvedCreatedAt = serverCreatedAt ?: now
                        val resolvedUpdatedAt = serverUpdatedAt ?: resolvedCreatedAt
                        val resolvedSoldAt = if (status == "SOLD") serverSoldAt else null
                        val newItem = com.example.myapplication.data.local.ItemEntity(
                            id = id,
                            title = title,
                            description = description,
                            status = status,
                            price = price,
                            soldPrice = soldPrice,
                            shippingCost = if (obj.has("shippingCost") && !obj.isNull("shippingCost")) obj.optInt("shippingCost", 210) else 210,
                            feeRatePercent = if (obj.has("feeRatePercent") && !obj.isNull("feeRatePercent")) obj.optInt("feeRatePercent", 5) else 5,
                            expenseCost = if (obj.has("expenseCost") && !obj.isNull("expenseCost")) obj.optInt("expenseCost", 55) else 55,
                            useCustomSettings = if (obj.has("useCustomSettings") && !obj.isNull("useCustomSettings")) obj.optBoolean("useCustomSettings", false) else false,
                            mergedParentId = mergedParentId,
                            mergedChildCount = mergedChildCount,
                            mergedChildTitles = mergedChildTitles,
                            originalTotalBundlePrice = originalTotalBundlePrice,
                            discountAmount = discountAmount,
                            bundleLevel = bundleLevel,
                            isShared = true,
                            createdAt = resolvedCreatedAt,
                            updatedAt = resolvedUpdatedAt,
                            soldAt = resolvedSoldAt
                        )
                        dao.insertItem(newItem)
                    }

                    // 写真情報の同期（PCでの追加・削除・0枚化にも追従し、ローカルファイルパス自動補完）
                    val photosArray = obj.optJSONArray("photos")
                    val currentPhotos = dao.getPhotosByItemId(id)
                    val hasUnpushedLocalPhoto = currentPhotos.any {
                        !it.isPcSynced && !it.isOriginalDeleted && java.io.File(it.filePath).exists()
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
                            dao.deletePhotosByItemId(id)
                        }
                    } else if (photosArray != null && photosArray.length() > 0) {
                        val shouldUpdatePhotos = (existing == null) ||
                            (!hasUnpushedLocalPhoto && (hashListDiffers || hasInvalidDisplay))

                        if (shouldUpdatePhotos) {
                            val newPhotoEntities = mutableListOf<com.example.myapplication.data.local.PhotoEntity>()
                            for (pIdx in 0 until photosArray.length()) {
                                val pObj = photosArray.getJSONObject(pIdx)
                                val md5 = pObj.optString("md5Hash", "").trim()
                                val childId = pObj.optInt("childItemId", 0)
                                var localFilePath = ""
                                var localThumbPath: String? = null

                                // 1. 同一商品既存写真 または 全既存写真から MD5 ハッシュで実ファイルが存在するものを検索
                                if (md5.isNotBlank()) {
                                    val matched = (currentPhotos + allExistingPhotos).firstOrNull {
                                        it.md5Hash.equals(md5, ignoreCase = true) &&
                                        (java.io.File(it.filePath).exists() || (!it.thumbnailPath.isNullOrBlank() && java.io.File(it.thumbnailPath).exists()))
                                    }
                                    if (matched != null) {
                                        val origFile = if (matched.filePath.isNotBlank()) java.io.File(matched.filePath) else null
                                        val thumbFile = if (!matched.thumbnailPath.isNullOrBlank()) java.io.File(matched.thumbnailPath) else null
                                        if (origFile != null && origFile.exists()) {
                                            localFilePath = matched.filePath
                                            localThumbPath = matched.thumbnailPath
                                        } else if (thumbFile != null && thumbFile.exists()) {
                                            localFilePath = matched.thumbnailPath ?: ""
                                            localThumbPath = matched.thumbnailPath
                                        } else {
                                            localFilePath = matched.filePath
                                            localThumbPath = matched.thumbnailPath
                                        }
                                    }
                                }

                                // 2. MD5 で見つからず、childItemId が指定されている場合は元子商品の写真から引き当て
                                if (localFilePath.isBlank() && childId > 0) {
                                    val childPhotos = dao.getPhotosByItemId(childId)
                                    val matchedChildPhoto = childPhotos.firstOrNull { cp ->
                                        (md5.isNotBlank() && cp.md5Hash.equals(md5, ignoreCase = true)) ||
                                        java.io.File(cp.filePath).exists() ||
                                        (!cp.thumbnailPath.isNullOrBlank() && java.io.File(cp.thumbnailPath).exists())
                                    } ?: childPhotos.getOrNull(pIdx) ?: childPhotos.firstOrNull()

                                    if (matchedChildPhoto != null) {
                                        val cOrig = java.io.File(matchedChildPhoto.filePath)
                                        val cThumb = if (!matchedChildPhoto.thumbnailPath.isNullOrBlank()) java.io.File(matchedChildPhoto.thumbnailPath) else null
                                        if (cOrig.exists()) {
                                            localFilePath = matchedChildPhoto.filePath
                                            localThumbPath = matchedChildPhoto.thumbnailPath
                                        } else if (cThumb != null && cThumb.exists()) {
                                            localFilePath = matchedChildPhoto.thumbnailPath ?: ""
                                            localThumbPath = matchedChildPhoto.thumbnailPath
                                        } else {
                                            localFilePath = matchedChildPhoto.filePath
                                            localThumbPath = matchedChildPhoto.thumbnailPath
                                        }
                                    }
                                }

                                newPhotoEntities.add(
                                    com.example.myapplication.data.local.PhotoEntity(
                                        itemId = id,
                                        filePath = localFilePath,
                                        thumbnailPath = localThumbPath,
                                        isOriginalDeleted = localFilePath.isBlank() && !localThumbPath.isNullOrBlank(),
                                        isPcSynced = true,
                                        md5Hash = md5,
                                        syncStatus = "SYNCED",
                                        displayOrder = pIdx
                                    )
                                )
                            }

                            if (newPhotoEntities.isNotEmpty()) {
                                dao.deletePhotosByItemId(id)
                                dao.insertPhotos(newPhotoEntities)
                            }
                        }
                    }

                    // 欠損または破損サムネイル（PC追加写真含む）を検出した場合はサーバーから自動自己修復
                    val syncedPhotos = dao.getPhotosByItemId(id)
                    if (syncedPhotos.any { isPhotoThumbnailDamagedOrMissing(it) }) {
                        repairMissingOrCorruptThumbnailsForItem(id, cleanUrl, token)
                    }
                }
                true
            }
        } catch (e: Exception) {
            android.util.Log.e("PC_SYNC", "fetchServerItems 例外: ${e.message}", e)
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

