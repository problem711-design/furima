package com.example

import android.content.Context
import android.graphics.Bitmap
import android.net.Uri
import androidx.test.core.app.ApplicationProvider
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.AppDatabase
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.server.PcServerClient
import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import java.io.BufferedInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.ServerSocket
import java.net.Socket
import java.nio.charset.StandardCharsets
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [36])
class PhotoSyncIntegrityBuild17Test {

    private lateinit var context: Context
    private lateinit var db: AppDatabase
    private lateinit var repository: FleaMarketRepository
    private lateinit var pcServerClient: PcServerClient
    private val createdTempFiles = mutableListOf<File>()

    private fun resetDatabaseSingleton() {
        try {
            val instanceField = AppDatabase::class.java.getDeclaredField("INSTANCE")
            instanceField.isAccessible = true
            (instanceField.get(null) as? AppDatabase)?.close()
            instanceField.set(null, null)
        } catch (_: Exception) {
        }
    }

    @Before
    fun setup() {
        context = ApplicationProvider.getApplicationContext()
        context.getSharedPreferences("flea_market_settings", Context.MODE_PRIVATE).edit().clear().commit()
        context.getSharedPreferences("flea_market_sync_tombstones", Context.MODE_PRIVATE).edit().clear().commit()
        context.getSharedPreferences("pc_sync_prefs", Context.MODE_PRIVATE).edit().clear().commit()

        resetDatabaseSingleton()
        context.deleteDatabase("flea_market_database")

        db = AppDatabase.getDatabase(context)
        repository = FleaMarketRepository(context)
        pcServerClient = PcServerClient(context, repository)
    }

    @After
    fun tearDown() {
        resetDatabaseSingleton()
        context.deleteDatabase("flea_market_database")
        createdTempFiles.forEach { if (it.exists()) it.delete() }
        File(context.filesDir, "item_photos").takeIf { it.exists() }?.deleteRecursively()
        File(context.filesDir, "Thumbnails").takeIf { it.exists() }?.deleteRecursively()
        File(context.filesDir, "thumbnails").takeIf { it.exists() }?.deleteRecursively()
    }

    private fun createTempJpegFile(name: String, width: Int = 240, height: Int = 240): File {
        val dir = File(context.cacheDir, "build17_test_images").apply { mkdirs() }
        val file = File(dir, name)
        val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, 90, out)
        bmp.recycle()
        file.writeBytes(out.toByteArray())
        createdTempFiles.add(file)
        return file
    }

    private fun withMockPcServer(
        handler: (method: String, path: String, headers: Map<String, String>, bodyText: String) -> Pair<Int, String>,
        block: suspend (baseUrl: String) -> Unit
    ) = runBlocking {
        val running = AtomicBoolean(true)
        val server = ServerSocket(0)
        val worker = Thread {
            while (running.get() && !server.isClosed) {
                val socket: Socket = try {
                    server.accept()
                } catch (_: Exception) {
                    break
                }
                Thread {
                    socket.use { s ->
                        val input = BufferedInputStream(s.getInputStream())
                        val headerBytes = ByteArrayOutputStream()
                        var prev3 = -1
                        var prev2 = -1
                        var prev1 = -1
                        while (true) {
                            val b = input.read()
                            if (b == -1) break
                            headerBytes.write(b)
                            if (prev3 == '\r'.code && prev2 == '\n'.code && prev1 == '\r'.code && b == '\n'.code) {
                                break
                            }
                            prev3 = prev2
                            prev2 = prev1
                            prev1 = b
                        }
                        val headerStr = headerBytes.toString(StandardCharsets.UTF_8.name())
                        val lines = headerStr.split("\r\n").filter { it.isNotBlank() }
                        if (lines.isEmpty()) return@Thread
                        val parts = lines[0].split(" ")
                        val method = parts.getOrNull(0) ?: "GET"
                        val path = parts.getOrNull(1) ?: "/"
                        val headers = mutableMapOf<String, String>()
                        for (line in lines.drop(1)) {
                            val idx = line.indexOf(':')
                            if (idx > 0) {
                                headers[line.substring(0, idx).trim().lowercase()] = line.substring(idx + 1).trim()
                            }
                        }
                        val contentLength = headers["content-length"]?.toIntOrNull() ?: 0
                        val bodyBytes = ByteArray(contentLength)
                        var readTotal = 0
                        while (readTotal < contentLength) {
                            val r = input.read(bodyBytes, readTotal, contentLength - readTotal)
                            if (r <= 0) break
                            readTotal += r
                        }
                        val isBinaryPhoto = path.startsWith("/api/sync/photo")
                        val bodyText = if (isBinaryPhoto) "<binary:$readTotal>" else String(bodyBytes, 0, readTotal, StandardCharsets.UTF_8)
                        val (statusCode, jsonBody) = handler(method, path, headers, bodyText)
                        val respBytes = jsonBody.toByteArray(StandardCharsets.UTF_8)
                        val reason = if (statusCode == 200) "OK" else "Error"
                        val respHeader = "HTTP/1.1 $statusCode $reason\r\n" +
                            "Content-Type: application/json; charset=utf-8\r\n" +
                            "Content-Length: ${respBytes.size}\r\n" +
                            "Connection: close\r\n\r\n"
                        s.getOutputStream().write(respHeader.toByteArray(StandardCharsets.US_ASCII))
                        s.getOutputStream().write(respBytes)
                        s.getOutputStream().flush()
                    }
                }.start()
            }
        }.apply { start() }

        try {
            block("http://127.0.0.1:${server.localPort}")
        } finally {
            running.set(false)
            try { server.close() } catch (_: Exception) {}
            worker.join(2000)
        }
    }

    @Test
    fun `saveItem skips nonexistent and 0-byte existingFilePaths and reports skipped count`() = runBlocking {
        val validImg = createTempJpegFile("valid_photo.jpg")
        val zeroByteFile = File(context.cacheDir, "empty_photo.jpg").apply {
            parentFile?.mkdirs()
            writeBytes(ByteArray(0))
            createdTempFiles.add(this)
        }
        val missingPath = File(context.filesDir, "item_photos/never_created_999.jpg").absolutePath

        var skippedCallbackCount = 0
        val itemId = repository.saveItem(
            title = "欠損パス除外テスト",
            description = "壊れパスをDBに入れない検証",
            status = "DRAFT",
            price = 1200,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(validImg)),
            existingFilePaths = listOf(missingPath, zeroByteFile.absolutePath),
            onSkippedInvalidPhotos = { skippedCallbackCount = it }
        ).toInt()

        assertEquals(2, skippedCallbackCount)
        val saved = repository.getItemById(itemId)!!
        assertEquals(1, saved.photos.size)
        assertTrue(saved.photos.all { it.md5Hash.isNotBlank() })
    }

    @Test
    fun `saveItem resolves switched THUMB_ path when Step 3 runs before save`() = runBlocking {
        val tempPhoto = createTempJpegFile("thumb_fallback_check.jpg")
        val itemId = repository.saveItem(
            title = "サムネフォールバック検証",
            description = "原本からサムネ切替後の保存",
            status = "LISTED",
            price = 2200,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(tempPhoto))
        ).toInt()

        val initialPhoto = repository.getItemById(itemId)!!.photos.first()
        val origPath = initialPhoto.filePath
        val origThumbPath = initialPhoto.thumbnailPath!!
        val origMd5 = initialPhoto.md5Hash

        // Step 3 が画面側の oldPath を裏でサムネイルに差し替えたケースを模擬
        assertTrue(File(origPath).delete())
        repository.getDao().markOriginalDeletedByFilePath(origPath, origThumbPath)

        var skippedCount = 0
        repository.saveItem(
            id = itemId,
            title = "サムネフォールバック検証・更新",
            description = "原本パス文字列が画面に残っていたケース",
            status = "LISTED",
            price = 2100,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = emptyList(),
            existingFilePaths = listOf(origPath),
            onSkippedInvalidPhotos = { skippedCount = it }
        )

        assertEquals(0, skippedCount)
        val after = repository.getItemById(itemId)!!.photos
        assertEquals(1, after.size)
        assertEquals(origMd5, after.first().md5Hash)
        assertTrue(after.first().isOriginalDeleted)
        assertEquals(origThumbPath, after.first().thumbnailPath)
    }

    @Test
    fun `cleanupGhostBlankMd5Photos removes pure ghosts but preserves existing files and edited items`() = runBlocking {
        val realImg = createTempJpegFile("ghost_cleanup_keep.jpg")
        val itemId = repository.saveItem(
            title = "ゴースト浄化検証",
            description = "空MD5孤立行のみ除去",
            status = "DRAFT",
            price = 1500,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(realImg))
        ).toInt()

        val editedItemId = repository.saveItem(
            title = "編集中アイテム",
            description = "編集中は除外しない",
            status = "DRAFT",
            price = 1500,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = emptyList()
        ).toInt()

        val missingGhostPath = File(context.filesDir, "item_photos/ghost_missing.jpg").absolutePath
        val existingUncomputedFile = createTempJpegFile("existing_blank_md5.jpg")

        repository.getDao().insertPhotos(
            listOf(
                PhotoEntity(
                    itemId = itemId,
                    filePath = missingGhostPath,
                    thumbnailPath = null,
                    md5Hash = "",
                    displayOrder = 1
                ),
                PhotoEntity(
                    itemId = itemId,
                    filePath = existingUncomputedFile.absolutePath,
                    thumbnailPath = null,
                    md5Hash = "",
                    displayOrder = 2
                ),
                PhotoEntity(
                    itemId = editedItemId,
                    filePath = File(context.filesDir, "item_photos/edited_ghost.jpg").absolutePath,
                    thumbnailPath = null,
                    md5Hash = "",
                    displayOrder = 0
                )
            )
        )

        repository.setItemEditingActive(editedItemId, true)
        val removed = repository.cleanupGhostBlankMd5Photos()
        repository.setItemEditingActive(editedItemId, false)

        assertEquals(1, removed)
        val itemPhotos = repository.getItemById(itemId)!!.photos
        assertEquals(2, itemPhotos.size)
        assertTrue(itemPhotos.none { it.filePath == missingGhostPath })
        assertTrue(itemPhotos.any { it.filePath == existingUncomputedFile.absolutePath })
        assertEquals(1, repository.getItemById(editedItemId)!!.photos.size)
    }

    @Test
    fun `syncDeltaToPcServer skips blank md5, keeps isDirty when upload fails, clears isDirty on upload ok, and protects editing Step 3`() = runBlocking {
        val validImg = createTempJpegFile("sync_valid_item.jpg")
        val itemId = repository.saveItem(
            title = "同期テスト商品",
            description = "Step2完了後isDirty解除検証",
            status = "LISTED",
            price = 3000,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(validImg))
        ).toInt()

        val savedPhoto = repository.getItemById(itemId)!!.photos.first()
        val expectedMd5 = savedPhoto.md5Hash
        val origFilePath = savedPhoto.filePath

        // 編集中ゴースト (Step1 で除外されるべき) と実ファイル付き写真を用意
        val pushedSyncPayloads = CopyOnWriteArrayList<String>()
        val failPhotoUpload = AtomicBoolean(true)

        withMockPcServer({ method, path, headers, bodyText ->
            when {
                method == "POST" && path == "/api/sync/items" -> {
                    pushedSyncPayloads.add(bodyText)
                    200 to JSONObject().apply {
                        put("status", "ok")
                        put("storeId", "store-build17-test")
                        put("syncedHashes", JSONArray())
                        put("neededHashes", JSONArray().put(expectedMd5))
                        put("permanentlyDeletedUuids", JSONArray())
                    }.toString()
                }
                method == "POST" && path == "/api/sync/photo" -> {
                    val hashHeader = headers["x-photo-hash"] ?: expectedMd5
                    if (failPhotoUpload.get()) {
                        500 to """{"status":"error","stored":false}"""
                    } else {
                        200 to JSONObject().apply {
                            put("status", "ok")
                            put("stored", true)
                            put("hash", hashHeader)
                        }.toString()
                    }
                }
                else -> 404 to """{"status":"error"}"""
            }
        }) { baseUrl ->
            // 1. Step2 写真送信失敗時: isDirty は true のまま維持され、原本も消えない
            val res1 = pcServerClient.syncDeltaToPcServer(baseUrl, "tok")
            assertTrue(res1.isSuccess)
            val afterFail = repository.getItemById(itemId)!!
            assertTrue("Step2失敗時はisDirty=trueを維持すべき", afterFail.item.isDirty)
            assertFalse(afterFail.photos.first().isOriginalDeleted)
            assertTrue(File(origFilePath).exists())

            // ペイロード内の photos に空 md5Hash が含まれないことを確認
            val payloadJson = JSONObject(pushedSyncPayloads.first())
            val firstItemPhotos = payloadJson.getJSONArray("items").getJSONObject(0).getJSONArray("photos")
            assertEquals(1, firstItemPhotos.length())
            assertEquals(expectedMd5, firstItemPhotos.getJSONObject(0).getString("md5Hash"))

            // 2. Step2 成功時でも、アイテム編集中 (setItemEditingActive) なら isDirty=false にはなるが Step3 原本削除はスキップ
            failPhotoUpload.set(false)
            repository.setItemEditingActive(itemId, true)
            val res2 = pcServerClient.syncDeltaToPcServer(baseUrl, "tok")
            repository.setItemEditingActive(itemId, false)

            assertTrue(res2.isSuccess)
            val afterProtectedSync = repository.getItemById(itemId)!!
            assertFalse("Step2完了後はisDirty=falseに遷移すべき", afterProtectedSync.item.isDirty)
            assertFalse("編集中はStep3の原本削除を抑止すべき", afterProtectedSync.photos.first().isOriginalDeleted)
            assertTrue(File(origFilePath).exists())
        }
    }

    @Test
    fun `mergeServerItemsFromJson ignores blank md5Hash photo entries from server json`() = runBlocking {
        val now = System.currentTimeMillis()
        val serverArray = JSONArray().put(
            JSONObject().apply {
                put("id", 501)
                put("syncUuid", "uuid-server-blank-md5-501")
                put("title", "サーバー空MD5混入耐性テスト")
                put("description", "テスト")
                put("status", "LISTED")
                put("price", 1800)
                put("soldPrice", 0)
                put("createdAt", now)
                put("updatedAt", now)
                put(
                    "photos",
                    JSONArray().apply {
                        put(JSONObject().apply {
                            put("md5Hash", "")
                            put("photoUrl", "/api/photos/")
                            put("displayOrder", 0)
                        })
                        put(JSONObject().apply {
                            put("md5Hash", "   ")
                            put("photoUrl", "/api/photos/")
                            put("displayOrder", 1)
                        })
                    }
                )
            }
        )

        pcServerClient.mergeServerItemsFromJson(
            jsonArray = serverArray,
            permanentlyDeletedUuids = emptySet(),
            repairThumbnails = false
        )

        val merged = repository.getDao().getItemWithPhotosBySyncUuid("uuid-server-blank-md5-501")
        assertNotNull(merged)
        assertEquals(0, merged!!.photos.size)
    }
}
