package com.example.myapplication.data.server

import android.content.Context
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.PhotoStorageManager
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

            // スマホ側の全商品・写真リストを取得（同期対象は通常アクティブ商品）
            val itemsWithPhotos = repository.getActiveItemsWithPhotosDirect()
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
                        photos = photoDtoList
                    )
                )
            }

            // 1. POST /api/sync/items
            val syncReq = SyncItemsRequest(
                protocolVersion = 3,
                storeId = savedStoreId,
                items = dtoList,
                activeFolderNames = activeFolderNames
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
                                    repository.getDao().updatePhotoPcSynced(photo.id, true)
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
}
