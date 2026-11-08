package com.example.myapplication.data.server

import android.content.Context
import android.util.Base64
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.PhotoStorageManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
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

    fun getSavedStoreId(): String {
        return prefs.getString("pc_store_id", "") ?: ""
    }

    fun saveStoreId(storeId: String) {
        prefs.edit().putString("pc_store_id", storeId).apply()
    }

    suspend fun checkServerStatus(baseUrl: String): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val url = "$cleanUrl/api/server/status"
            val request = Request.Builder().url(url).get().build()
            client.newCall(request).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            false
        }
    }

    suspend fun sendHeartbeat(baseUrl: String): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val url = "$cleanUrl/api/server/heartbeat"
            val request = Request.Builder().url(url).get().build()
            client.newCall(request).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            false
        }
    }

    suspend fun fetchServerHashes(baseUrl: String): Set<String> = withContext(Dispatchers.IO) {
        val hashSet = mutableSetOf<String>()
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext hashSet
            val url = "$cleanUrl/api/sync/hashes"
            val request = Request.Builder().url(url).get().build()
            client.newCall(request).execute().use { response ->
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
     * PCサーバーに未保持の差分写真および全商品メタデータを確実に送信し、
     * 応答 storeId の整合性とサーバー保存後計算 MD5 を検証してスマホ側元画像を安全に容量削減。
     */
    suspend fun syncDeltaToPcServer(baseUrl: String): PcSyncResult = withContext(Dispatchers.IO) {
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
            android.util.Log.i("PC_SYNC", "=== PUSH同期開始: $cleanUrl ===")
            val savedStoreId = getSavedStoreId()

            // 1. PC側で既に保持されている写真のMD5ハッシュ一覧を取得
            val existingServerHashes = fetchServerHashes(cleanUrl)
            android.util.Log.i("PC_SYNC", "PC側既存ハッシュ数: ${existingServerHashes.size}")

            // 2. スマホ側の全商品・写真リストを取得
            val itemsWithPhotos = repository.getActiveItemsWithPhotosDirect()
            android.util.Log.i("PC_SYNC", "スマホ側対象商品数: ${itemsWithPhotos.size}")
            val itemsJsonArray = JSONArray()
            val activeFolderNames = JSONArray()

            for (itemWithPhotos in itemsWithPhotos) {
                val item = itemWithPhotos.item
                val photos = itemWithPhotos.photos

                val safeTitle = item.title.trim().replace(Regex("[\\\\/:*?\"<>|]"), "_")
                val itemFolderName = "${item.id}_$safeTitle"
                activeFolderNames.put(itemFolderName)

                val itemObj = JSONObject().apply {
                    put("id", item.id)
                    put("title", item.title)
                    put("description", item.description)
                    put("status", item.status)
                    put("price", item.price)
                    put("soldPrice", item.soldPrice)
                    put("profit", item.getCalculatedProfit(repository.defaultSettings.value))
                    put("statusLabel", when (item.status) {
                        "SOLD" -> "売却済み"
                        "MERGED" -> "まとめ売却"
                        "DRAFT" -> "未出品"
                        else -> "出品中"
                    })
                }

                val photosArray = JSONArray()
                for (photo in photos) {
                    // MD5ハッシュが空の場合はその場で自動計算し、DBに永続保存
                    var md5 = photo.md5Hash
                    val file = File(photo.filePath)
                    if (md5.isBlank() && file.exists()) {
                        md5 = photoStorageManager.calculateMd5(file)
                        if (md5.isNotBlank()) {
                            repository.getDao().updatePhotoMd5Hash(photo.id, md5)
                        }
                    }

                    val photoObj = JSONObject().apply {
                        put("id", photo.id)
                        put("md5Hash", md5)
                    }

                    // PC側に未保存かつスマホ側に元画像が存在する場合、Base64転送用に読み込み
                    if (md5.isNotBlank() && !existingServerHashes.contains(md5) && !photo.isOriginalDeleted) {
                        if (file.exists()) {
                            val bytes = file.readBytes()
                            val base64Str = Base64.encodeToString(bytes, Base64.NO_WRAP)
                            photoObj.put("base64Data", base64Str)
                        }
                    }

                    photosArray.put(photoObj)
                }

                itemObj.put("photos", photosArray)
                itemsJsonArray.put(itemObj)
            }

            val payload = JSONObject().apply {
                put("timestamp", System.currentTimeMillis())
                put("storeId", savedStoreId)
                put("items", itemsJsonArray)
                put("activeFolderNames", activeFolderNames)
            }

            val pushUrl = "$cleanUrl/api/sync/push"
            val payloadStr = payload.toString()
            android.util.Log.i("PC_SYNC", "Payload作成完了 (サイズ: ${payloadStr.length}文字). 送信先: $pushUrl")

            // 3. PCサーバーへPUSH送信
            val body = payloadStr.toRequestBody("application/json; charset=utf-8".toMediaType())
            val request = Request.Builder().url(pushUrl).post(body).build()

            client.newCall(request).execute().use { response ->
                android.util.Log.i("PC_SYNC", "サーバー応答コード: ${response.code}")
                if (!response.isSuccessful) {
                    return@withContext PcSyncResult(
                        isSuccess = false,
                        errorMessage = "サーバーエラー応答: HTTP ${response.code}"
                    )
                }

                val respStr = response.body?.string() ?: ""
                android.util.Log.i("PC_SYNC", "サーバー応答本文: $respStr")
                val respJson = JSONObject(respStr)

                val respStoreId = respJson.optString("storeId", "")
                if (savedStoreId.isBlank() && respStoreId.isNotBlank()) {
                    saveStoreId(respStoreId)
                }

                val syncedHashesArray = respJson.optJSONArray("syncedHashes")
                val syncedSet = mutableSetOf<String>()

                if (syncedHashesArray != null) {
                    for (i in 0 until syncedHashesArray.length()) {
                        syncedSet.add(syncedHashesArray.getString(i))
                    }
                }

                // 高画質削除の3条件検証
                // 条件1: 応答 storeId と 端末記憶 storeId の一致 (端末記憶が無い場合は保存して一致扱い)
                val currentStoreId = getSavedStoreId()
                val isStoreIdValid = currentStoreId.isNotBlank() && currentStoreId == respStoreId

                var missingOriginalCount = 0

                for (itemWithPhotos in itemsWithPhotos) {
                    for (photo in itemWithPhotos.photos) {
                        var md5 = photo.md5Hash
                        if (md5.isBlank()) {
                            val f = File(photo.filePath)
                            if (f.exists()) md5 = photoStorageManager.calculateMd5(f)
                        }

                        // 原本なし件数（サムネイル化済みだが、今回または既存のサーバーハッシュに存在しない）のカウント
                        if (photo.isOriginalDeleted) {
                            if (md5.isNotBlank() && !syncedSet.contains(md5) && !existingServerHashes.contains(md5)) {
                                missingOriginalCount++
                            }
                        }

                        // 高画質削除判定 (条件1: storeId一致, 条件2: サーバーが保存後自己計算した syncedSet に含まれる, 条件3: スマホ側にサムネイルが存在する)
                        if (isStoreIdValid && md5.isNotBlank() && syncedSet.contains(md5)) {
                            if (!photo.isOriginalDeleted) {
                                val file = File(photo.filePath)
                                val hasThumb = photo.thumbnailPath != null && File(photo.thumbnailPath).exists()
                                if (file.exists() && hasThumb) {
                                    val fileSize = file.length()
                                    if (file.delete()) {
                                        freedBytes += fileSize
                                        syncedPhotoCount++
                                        // DB更新: isOriginalDeleted = true, syncStatus = "SYNCED"
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
            }
        } catch (e: Exception) {
            android.util.Log.e("PC_SYNC", "PUSH同期で例外が発生しました: ${e.message}", e)
            e.printStackTrace()
            return@withContext PcSyncResult(
                isSuccess = false,
                errorMessage = e.localizedMessage ?: e.message ?: "通信接続エラーが発生しました"
            )
        }
    }
}
