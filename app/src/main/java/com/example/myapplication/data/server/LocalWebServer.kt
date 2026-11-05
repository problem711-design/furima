package com.example.myapplication.data.server

import android.content.Context
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemWithPhotos
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpMethod
import io.ktor.http.HttpStatusCode
import io.ktor.http.content.PartData
import io.ktor.http.content.forEachPart
import io.ktor.http.content.streamProvider
import io.ktor.serialization.gson.gson
import io.ktor.server.application.Application
import io.ktor.server.application.install
import io.ktor.server.cio.CIO
import io.ktor.server.engine.EmbeddedServer
import io.ktor.server.engine.embeddedServer
import io.ktor.server.plugins.contentnegotiation.ContentNegotiation
import io.ktor.server.plugins.cors.routing.CORS
import io.ktor.server.request.receive
import io.ktor.server.request.receiveMultipart
import io.ktor.server.response.respond
import io.ktor.server.response.respondFile
import io.ktor.server.response.respondText
import io.ktor.server.routing.delete
import io.ktor.server.routing.get
import io.ktor.server.routing.post
import io.ktor.server.routing.put
import kotlinx.coroutines.flow.first
import io.ktor.server.routing.routing
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class LocalWebServer(
    private val context: Context,
    private val repository: FleaMarketRepository,
    val port: Int = 8080
) {
    private var serverEngine: EmbeddedServer<*, *>? = null

    private val _isRunning = MutableStateFlow(false)
    val isRunning: StateFlow<Boolean> = _isRunning.asStateFlow()

    private val _logs = MutableStateFlow<List<String>>(emptyList())
    val logs: StateFlow<List<String>> = _logs.asStateFlow()

    private fun addLog(message: String) {
        val time = SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date())
        val logEntry = "[$time] $message"
        _logs.value = (_logs.value + logEntry).takeLast(100)
    }

    fun start(): Boolean {
        if (_isRunning.value) return true
        return try {
            val engine = embeddedServer(CIO, port = port, host = "0.0.0.0") {
                configurePlugins()
                configureRouting()
            }
            engine.start(wait = false)
            serverEngine = engine

            _isRunning.value = true
            addLog("Webサーバーを起動しました (ポート $port)")
            true
        } catch (e: Exception) {
            e.printStackTrace()
            addLog("起動エラー: ${e.localizedMessage}")
            _isRunning.value = false
            false
        }
    }

    fun stop() {
        if (!_isRunning.value) return
        try {
            serverEngine?.stop(1000, 2000)
            serverEngine = null
            _isRunning.value = false
            addLog("Webサーバーを停止しました")
        } catch (e: Exception) {
            e.printStackTrace()
            addLog("停止エラー: ${e.localizedMessage}")
        }
    }

    private fun Application.configurePlugins() {
        install(CORS) {
            allowMethod(HttpMethod.Options)
            allowMethod(HttpMethod.Put)
            allowMethod(HttpMethod.Delete)
            allowMethod(HttpMethod.Patch)
            allowMethod(HttpMethod.Post)
            allowMethod(HttpMethod.Get)
            allowHeader(HttpHeaders.Authorization)
            allowHeader(HttpHeaders.ContentType)
            anyHost()
        }
        install(ContentNegotiation) {
            gson {
                setPrettyPrinting()
                serializeNulls()
            }
        }
    }

    private fun Application.configureRouting() {
        routing {
            // PCブラウザ用SPA Web管理画面
            get("/") {
                addLog("PCがWeb管理画面にアクセスしました")
                call.respondText(WebDashboardHtml.getHtml(), ContentType.Text.Html)
            }

            // サーバー状態確認
            get("/api/status") {
                call.respond(mapOf("status" to "ok", "app" to "FleaMarket PC Sync"))
            }

            // 商品一覧
            get("/api/items") {
                try {
                    val settings = repository.defaultSettings.value
                    val items = repository.getActiveItemsWithPhotosDirect()
                    val dtoList = items.map { it.toDto(settings) }
                    call.respond(dtoList)
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 商品詳細取得
            get("/api/items/{id}") {
                val id = call.parameters["id"]?.toIntOrNull()
                if (id == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid ID"))
                    return@get
                }
                val item = repository.getItemById(id)
                if (item != null) {
                    call.respond(item.toDto(repository.defaultSettings.value))
                } else {
                    call.respond(HttpStatusCode.NotFound, mapOf("error" to "Item not found"))
                }
            }

            // 商品更新
            put("/api/items/{id}") {
                val id = call.parameters["id"]?.toIntOrNull()
                if (id == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid ID"))
                    return@put
                }
                try {
                    val body = call.receive<Map<String, Any?>>()
                    val existing = repository.getItemById(id)
                    if (existing == null) {
                        call.respond(HttpStatusCode.NotFound, mapOf("error" to "Item not found"))
                        return@put
                    }

                    val title = body["title"] as? String ?: existing.item.title
                    val description = body["description"] as? String ?: existing.item.description
                    val status = body["status"] as? String ?: existing.item.status
                    val price = (body["price"] as? Number)?.toInt() ?: existing.item.price
                    val soldPrice = (body["soldPrice"] as? Number)?.toInt() ?: existing.item.soldPrice
                    val shippingCost = (body["shippingCost"] as? Number)?.toInt() ?: existing.item.shippingCost
                    val feeRatePercent = (body["feeRatePercent"] as? Number)?.toInt() ?: existing.item.feeRatePercent
                    val expenseCost = (body["expenseCost"] as? Number)?.toInt() ?: existing.item.expenseCost
                    val useCustomSettings = (body["useCustomSettings"] as? Boolean) ?: existing.item.useCustomSettings

                    val existingPaths = existing.photos.map { it.filePath }
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
                        imageUris = emptyList(),
                        existingFilePaths = existingPaths
                    )
                    addLog("PCから商品を更新: $title (ID: $id)")
                    call.respond(mapOf("success" to true, "id" to id))
                } catch (e: Exception) {
                    e.printStackTrace()
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // ステータス更新（出品中・売却済みのワンクリック切り替え）
            post("/api/items/{id}/status") {
                val id = call.parameters["id"]?.toIntOrNull()
                if (id == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid ID"))
                    return@post
                }
                try {
                    val body = call.receive<Map<String, Any?>>()
                    val newStatus = body["status"] as? String ?: "LISTED"
                    val soldPrice = (body["soldPrice"] as? Number)?.toInt()

                    val existing = repository.getItemById(id)
                    if (existing == null) {
                        call.respond(HttpStatusCode.NotFound, mapOf("error" to "Item not found"))
                        return@post
                    }

                    var updatedItem = existing.item.copy(
                        status = newStatus,
                        soldAt = if (newStatus == "SOLD") (existing.item.soldAt ?: System.currentTimeMillis()) else null
                    )
                    if (soldPrice != null && soldPrice > 0) {
                        updatedItem = updatedItem.copy(soldPrice = soldPrice)
                    }
                    repository.getDao().updateItem(updatedItem)

                    addLog("PCからステータス変更: ${existing.item.title} -> $newStatus")
                    call.respond(mapOf("success" to true, "status" to newStatus))
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 商品削除
            delete("/api/items/{id}") {
                val id = call.parameters["id"]?.toIntOrNull()
                if (id == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid ID"))
                    return@delete
                }
                try {
                    val existing = repository.getItemById(id)
                    if (existing != null) {
                        repository.deleteItem(existing)
                        addLog("PCから商品を削除: ${existing.item.title} (ID: $id)")
                        call.respond(mapOf("success" to true))
                    } else {
                        call.respond(HttpStatusCode.NotFound, mapOf("error" to "Item not found"))
                    }
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 新規商品作成 (マルチパート: JSON情報 + 画像ファイル群)
            post("/api/items") {
                try {
                    val multipart = call.receiveMultipart()
                    var title = ""
                    var description = ""
                    var status = "LISTED"
                    var price = 0
                    var soldPrice = 0
                    var shippingCost = repository.defaultSettings.value.defaultShippingCost
                    var feeRatePercent = repository.defaultSettings.value.defaultFeeRatePercent
                    var expenseCost = repository.defaultSettings.value.defaultExpenseCost
                    var useCustomSettings = false

                    val uploadedPhotoFiles = mutableListOf<File>()

                    multipart.forEachPart { part ->
                        when (part) {
                            is PartData.FormItem -> {
                                when (part.name) {
                                    "title" -> title = part.value
                                    "description" -> description = part.value
                                    "status" -> status = part.value
                                    "price" -> price = part.value.toIntOrNull() ?: 0
                                    "soldPrice" -> soldPrice = part.value.toIntOrNull() ?: 0
                                    "shippingCost" -> shippingCost = part.value.toIntOrNull() ?: shippingCost
                                    "feeRatePercent" -> feeRatePercent = part.value.toIntOrNull() ?: feeRatePercent
                                    "expenseCost" -> expenseCost = part.value.toIntOrNull() ?: expenseCost
                                    "useCustomSettings" -> useCustomSettings = part.value.toBoolean()
                                }
                            }
                            is PartData.FileItem -> {
                                val fileName = part.originalFileName ?: "photo_${System.currentTimeMillis()}.jpg"
                                val tempDir = File(this@LocalWebServer.context.cacheDir, "web_uploads")
                                if (!tempDir.exists()) tempDir.mkdirs()
                                val tempFile = File(tempDir, "${System.currentTimeMillis()}_$fileName")
                                val streamProvider = part.streamProvider
                                val inputStream: InputStream = streamProvider()
                                inputStream.use { input ->
                                    FileOutputStream(tempFile).use { output ->
                                        input.copyTo(output)
                                    }
                                }
                                uploadedPhotoFiles.add(tempFile)
                            }
                            else -> {}
                        }
                        part.dispose()
                    }

                    if (title.isBlank()) {
                        title = "PC登録商品_" + SimpleDateFormat("MMdd_HHmm", Locale.getDefault()).format(Date())
                    }

                    val savedPhotoPaths = mutableListOf<String>()
                    val photoStorageManager = repository.getPhotoStorageManager()
                    val targetDir = photoStorageManager.getItemImageDir(title)

                    for (tempFile in uploadedPhotoFiles) {
                        val originalFile = File(targetDir, "PC_${tempFile.name}")
                        tempFile.copyTo(originalFile, overwrite = true)
                        tempFile.delete()
                        photoStorageManager.syncFileToPublicGallery(title, originalFile)
                        savedPhotoPaths.add(originalFile.absolutePath)
                    }

                    val createdId = repository.saveItem(
                        id = 0,
                        title = title,
                        description = description,
                        status = status,
                        price = price,
                        soldPrice = soldPrice,
                        shippingCost = shippingCost,
                        feeRatePercent = feeRatePercent,
                        expenseCost = expenseCost,
                        useCustomSettings = useCustomSettings,
                        imageUris = emptyList(),
                        existingFilePaths = savedPhotoPaths
                    )

                    addLog("PCから新規商品を作成: $title (ID: $createdId, 写真${savedPhotoPaths.size}枚)")
                    call.respond(mapOf("success" to true, "id" to createdId))
                } catch (e: Exception) {
                    e.printStackTrace()
                    addLog("商品作成エラー: ${e.localizedMessage}")
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 既存商品への写真追加アップロード (マルチパート)
            post("/api/items/{id}/photos") {
                val id = call.parameters["id"]?.toIntOrNull()
                if (id == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid ID"))
                    return@post
                }
                val existing = repository.getItemById(id)
                if (existing == null) {
                    call.respond(HttpStatusCode.NotFound, mapOf("error" to "Item not found"))
                    return@post
                }

                try {
                    val multipart = call.receiveMultipart()
                    val newPaths = mutableListOf<String>()
                    val photoStorageManager = repository.getPhotoStorageManager()
                    val targetDir = photoStorageManager.getItemImageDir(existing.item.title)

                    multipart.forEachPart { part ->
                        if (part is PartData.FileItem) {
                            val timeStamp = SimpleDateFormat("yyyyMMdd_HHmmss_SSS", Locale.getDefault()).format(Date())
                            val originalFile = File(targetDir, "PC_${timeStamp}.jpg")
                            val streamProvider = part.streamProvider
                            val inputStream: InputStream = streamProvider()
                            inputStream.use { input ->
                                FileOutputStream(originalFile).use { output ->
                                    input.copyTo(output)
                                }
                            }
                            photoStorageManager.syncFileToPublicGallery(existing.item.title, originalFile)
                            newPaths.add(originalFile.absolutePath)
                        }
                        part.dispose()
                    }

                    val allPaths = existing.photos.map { it.filePath } + newPaths
                    repository.saveItem(
                        id = existing.item.id,
                        title = existing.item.title,
                        description = existing.item.description,
                        status = existing.item.status,
                        price = existing.item.price,
                        soldPrice = existing.item.soldPrice,
                        shippingCost = existing.item.shippingCost,
                        feeRatePercent = existing.item.feeRatePercent,
                        expenseCost = existing.item.expenseCost,
                        useCustomSettings = existing.item.useCustomSettings,
                        imageUris = emptyList(),
                        existingFilePaths = allPaths
                    )

                    addLog("PCから写真を追加: ${existing.item.title} (+${newPaths.size}枚)")
                    call.respond(mapOf("success" to true, "addedCount" to newPaths.size))
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 送信済みフラグ更新
            post("/api/items/{id}/mark-shared") {
                val id = call.parameters["id"]?.toIntOrNull()
                if (id == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid ID"))
                    return@post
                }
                repository.markItemShared(id)
                addLog("PCから送信済みフラグを更新 (ID: $id)")
                call.respond(mapOf("success" to true))
            }

            // まとめ買い作成 (PCから)
            post("/api/items/merge") {
                try {
                    val body = call.receive<Map<String, Any?>>()
                    @Suppress("UNCHECKED_CAST")
                    val childIdsRaw = body["childItemIds"] as? List<Number>
                    val childItemIds = childIdsRaw?.map { it.toInt() } ?: emptyList()
                    if (childItemIds.size < 2) {
                        call.respond(HttpStatusCode.BadRequest, mapOf("error" to "At least 2 items required to merge"))
                        return@post
                    }

                    val mergedTitle = body["mergedTitle"] as? String ?: "まとめ買い商品"
                    val mergedDescription = body["mergedDescription"] as? String ?: ""
                    val totalSoldPrice = (body["totalSoldPrice"] as? Number)?.toInt() ?: 0
                    val shippingCost = (body["newShippingCost"] as? Number)?.toInt() ?: 210
                    val feeRatePercent = (body["newFeeRatePercent"] as? Number)?.toInt() ?: 5
                    val expenseCost = (body["newExpenseCost"] as? Number)?.toInt() ?: 55
                    val asStatus = body["asStatus"] as? String ?: "LISTED"

                    val parentId = repository.mergeItems(
                        mergedTitle = mergedTitle,
                        mergedDescription = mergedDescription,
                        totalSoldPrice = totalSoldPrice,
                        newShippingCost = shippingCost,
                        newFeeRatePercent = feeRatePercent,
                        newExpenseCost = expenseCost,
                        childItemIds = childItemIds,
                        asStatus = asStatus
                    )

                    addLog("PCからまとめ買いを作成: $mergedTitle (親ID: $parentId, 対象${childItemIds.size}点)")
                    call.respond(mapOf("success" to true, "parentId" to parentId))
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // まとめ買い解除 (バラす)
            post("/api/items/{id}/unmerge") {
                val id = call.parameters["id"]?.toIntOrNull()
                if (id == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid ID"))
                    return@post
                }
                try {
                    repository.unmergeItem(id)
                    addLog("PCからまとめ買いを解除 (親ID: $id)")
                    call.respond(mapOf("success" to true))
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 全件完全同期用API (PC自動同期ツール用)
            get("/api/sync/full-data") {
                try {
                    val settings = repository.defaultSettings.value
                    val items = repository.getActiveItemsWithPhotosDirect()
                    val dtoList = items.map { it.toDto(settings) }
                    call.respond(mapOf(
                        "serverTime" to System.currentTimeMillis(),
                        "totalItems" to dtoList.size,
                        "items" to dtoList
                    ))
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 元写真の復元アップロード
            post("/api/photos/{photoId}/restore") {
                val photoId = call.parameters["photoId"]?.toIntOrNull()
                if (photoId == null) {
                    call.respond(HttpStatusCode.BadRequest, mapOf("error" to "Invalid Photo ID"))
                    return@post
                }
                val photo = repository.getDao().getPhotoById(photoId)
                if (photo == null) {
                    call.respond(HttpStatusCode.NotFound, mapOf("error" to "Photo entity not found"))
                    return@post
                }

                try {
                    val multipart = call.receiveMultipart()
                    var restored = false
                    val photoStorageManager = repository.getPhotoStorageManager()

                    multipart.forEachPart { part ->
                        if (part is PartData.FileItem) {
                            val targetFile = File(photo.filePath)
                            targetFile.parentFile?.mkdirs()
                            val streamProvider = part.streamProvider
                            val inputStream: InputStream = streamProvider()
                            inputStream.use { input ->
                                FileOutputStream(targetFile).use { output ->
                                    input.copyTo(output)
                                }
                            }
                            // サムネイルも再作成
                            val newThumb = photoStorageManager.createThumbnail(
                                targetFile,
                                repository.defaultSettings.value.thumbnailMaxDimension,
                                repository.defaultSettings.value.thumbnailQuality
                            )
                            val updatedPhoto = photo.copy(
                                isOriginalDeleted = false,
                                thumbnailPath = newThumb ?: photo.thumbnailPath
                            )
                            repository.getDao().insertPhotos(listOf(updatedPhoto))
                            restored = true
                        }
                        part.dispose()
                    }

                    if (restored) {
                        addLog("PCから元高画質写真を復元保存 (PhotoID: $photoId)")
                        call.respond(mapOf("success" to true))
                    } else {
                        call.respond(HttpStatusCode.BadRequest, mapOf("error" to "No file provided"))
                    }
                } catch (e: Exception) {
                    call.respond(HttpStatusCode.InternalServerError, mapOf("error" to (e.localizedMessage ?: "Unknown error")))
                }
            }

            // 写真配信 (オリジナル)
            get("/api/photos/{id}") {
                val photoId = call.parameters["id"]?.toIntOrNull()
                if (photoId == null) {
                    call.respond(HttpStatusCode.BadRequest)
                    return@get
                }
                val photo = repository.getDao().getPhotoById(photoId)
                if (photo != null) {
                    val file = File(photo.filePath)
                    if (file.exists()) {
                        call.respondFile(file)
                        return@get
                    } else if (photo.thumbnailPath != null) {
                        val thumbFile = File(photo.thumbnailPath)
                        if (thumbFile.exists()) {
                            call.respondFile(thumbFile)
                            return@get
                        }
                    }
                }
                call.respond(HttpStatusCode.NotFound)
            }

            // サムネイル配信
            get("/api/photos/{id}/thumb") {
                val photoId = call.parameters["id"]?.toIntOrNull()
                if (photoId == null) {
                    call.respond(HttpStatusCode.BadRequest)
                    return@get
                }
                val photo = repository.getDao().getPhotoById(photoId)
                if (photo != null) {
                    if (photo.thumbnailPath != null) {
                        val thumbFile = File(photo.thumbnailPath)
                        if (thumbFile.exists()) {
                            call.respondFile(thumbFile)
                            return@get
                        }
                    }
                    val originalFile = File(photo.filePath)
                    if (originalFile.exists()) {
                        call.respondFile(originalFile)
                        return@get
                    }
                }
                call.respond(HttpStatusCode.NotFound)
            }
        }
    }

    private fun ItemWithPhotos.toDto(defaultSettings: DefaultSettings): WebItemDto {
        val profit = item.getCalculatedProfit(defaultSettings)
        val statusLabel = when (item.status) {
            "LISTED" -> "出品中"
            "SOLD" -> "売却済み"
            "DRAFT" -> "未出品"
            "MERGED" -> "まとめ売却"
            else -> item.status
        }
        val photoDtos = photos.map { photo ->
            WebPhotoDto(
                id = photo.id,
                url = "/api/photos/${photo.id}",
                thumbUrl = "/api/photos/${photo.id}/thumb",
                isOriginalDeleted = photo.isOriginalDeleted
            )
        }
        return WebItemDto(
            id = item.id,
            title = item.title,
            description = item.description,
            status = item.status,
            statusLabel = statusLabel,
            price = item.price,
            soldPrice = item.soldPrice,
            shippingCost = item.getEffectiveShippingCost(defaultSettings),
            feeRatePercent = item.getEffectiveFeeRatePercent(defaultSettings),
            expenseCost = item.getEffectiveExpenseCost(defaultSettings),
            profit = profit,
            isShared = item.isShared,
            createdAt = item.createdAt,
            soldAt = item.soldAt,
            photos = photoDtos
        )
    }
}

data class WebItemDto(
    val id: Int,
    val title: String,
    val description: String,
    val status: String,
    val statusLabel: String,
    val price: Int,
    val soldPrice: Int,
    val shippingCost: Int,
    val feeRatePercent: Int,
    val expenseCost: Int,
    val profit: Int,
    val isShared: Boolean,
    val createdAt: Long,
    val soldAt: Long?,
    val photos: List<WebPhotoDto>
)

data class WebPhotoDto(
    val id: Int,
    val url: String,
    val thumbUrl: String?,
    val isOriginalDeleted: Boolean
)
