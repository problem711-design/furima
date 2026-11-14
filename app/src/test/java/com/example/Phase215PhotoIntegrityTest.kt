package com.example

import android.content.Context
import android.graphics.Bitmap
import android.net.Uri
import androidx.test.core.app.ApplicationProvider
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.AppDatabase
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
import java.io.ByteArrayOutputStream
import java.io.File

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [36])
class Phase215PhotoIntegrityTest {

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
        createdTempFiles.forEach { file ->
            if (file.exists()) {
                file.delete()
            }
        }
        val photosDir = File(context.filesDir, "item_photos")
        if (photosDir.exists()) photosDir.deleteRecursively()
        val thumbDir = File(context.filesDir, "Thumbnails")
        if (thumbDir.exists()) thumbDir.deleteRecursively()
        val rootThumbDir = File(context.filesDir, "thumbnails")
        if (rootThumbDir.exists()) rootThumbDir.deleteRecursively()
    }

    private fun createTempJpegFile(name: String, width: Int = 240, height: Int = 240): File {
        val dir = File(context.cacheDir, "phase215_test_images").apply { mkdirs() }
        val file = File(dir, name)
        val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, 90, out)
        bmp.recycle()
        file.writeBytes(out.toByteArray())
        createdTempFiles.add(file)
        return file
    }

    @Test
    fun `resaving item after original deletion preserves original md5Hash and isOriginalDeleted`() = runBlocking {
        val tempPhoto = createTempJpegFile("orig_check.jpg")
        val itemId = repository.saveItem(
            title = "原本削除後の再保存検証",
            description = "初期説明",
            status = "LISTED",
            price = 2500,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(tempPhoto))
        ).toInt()

        val initialItem = repository.getItemById(itemId)!!
        val initialPhoto = initialItem.photos.first()
        val originalMd5 = initialPhoto.md5Hash
        val thumbPath = initialPhoto.thumbnailPath!!
        assertTrue(originalMd5.isNotBlank())
        assertTrue(File(thumbPath).exists())

        // 原本を削除して markOriginalDeletedByFilePath で更新（cleanupSharedOriginalPhotos と同じ操作）
        assertTrue(File(initialPhoto.filePath).delete())
        repository.getDao().markOriginalDeletedByFilePath(initialPhoto.filePath, thumbPath)

        val afterCleanup = repository.getItemById(itemId)!!.photos.first()
        assertTrue(afterCleanup.isOriginalDeleted)
        assertEquals(thumbPath, afterCleanup.filePath)
        assertEquals(originalMd5, afterCleanup.md5Hash)

        // ItemEditScreen からの再保存を再現（displayFile.absolutePath == thumbPath が existingFilePaths に渡される）
        repository.saveItem(
            id = itemId,
            title = "原本削除後の再保存検証（編集済み）",
            description = "説明文のみ更新",
            status = "LISTED",
            price = 2300,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = emptyList(),
            existingFilePaths = listOf(thumbPath)
        )

        val afterResave = repository.getItemById(itemId)!!.photos.first()
        assertEquals(originalMd5, afterResave.md5Hash)
        assertTrue(afterResave.isOriginalDeleted)
        assertEquals(thumbPath, afterResave.thumbnailPath)
    }

    @Test
    fun `markOriginalDeletedByFilePath updates both parent and merged child photo records sharing the same file`() = runBlocking {
        val tempPhoto = createTempJpegFile("shared_bundle.jpg")
        val childId = repository.saveItem(
            title = "まとめ対象子商品",
            description = "子商品",
            status = "LISTED",
            price = 1000,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(tempPhoto))
        ).toInt()

        val childPhoto = repository.getItemById(childId)!!.photos.first()
        val sharedFilePath = childPhoto.filePath
        val sharedThumbPath = childPhoto.thumbnailPath!!
        val sharedMd5 = childPhoto.md5Hash

        // 同一 filePath を共有するまとめ買い親商品を作成
        val parentId = repository.saveItem(
            title = "まとめ買い親商品",
            description = "親商品",
            status = "LISTED",
            price = 1800,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = emptyList(),
            existingFilePaths = listOf(sharedFilePath)
        ).toInt()
        repository.getDao().markItemsMerged(listOf(childId), parentId)

        // 親・子両方が同じ filePath を参照している状態から一括マーク
        assertTrue(File(sharedFilePath).delete())
        repository.getDao().markOriginalDeletedByFilePath(sharedFilePath, sharedThumbPath)

        val updatedParentPhoto = repository.getItemById(parentId)!!.photos.first()
        val updatedChildPhoto = repository.getItemById(childId)!!.photos.first()

        assertTrue(updatedParentPhoto.isOriginalDeleted)
        assertEquals(sharedThumbPath, updatedParentPhoto.filePath)
        assertEquals(sharedMd5, updatedParentPhoto.md5Hash)

        assertTrue(updatedChildPhoto.isOriginalDeleted)
        assertEquals(sharedThumbPath, updatedChildPhoto.filePath)
        assertEquals(sharedMd5, updatedChildPhoto.md5Hash)
    }

    @Test
    fun `mergeServerItemsFromJson keeps isOriginalDeleted true when only thumbnail exists locally`() = runBlocking {
        val tempPhoto = createTempJpegFile("pull_thumb_only.jpg")
        val itemId = repository.saveItem(
            title = "PC更新Pullテスト商品",
            description = "原本削除済み",
            status = "LISTED",
            price = 3000,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(tempPhoto))
        ).toInt()

        val savedItem = repository.getItemById(itemId)!!
        val photo = savedItem.photos.first()
        val origMd5 = photo.md5Hash
        val thumbPath = photo.thumbnailPath!!

        // 原本削除を完了し、isDirty = false（同期済み）にする
        assertTrue(File(photo.filePath).delete())
        repository.getDao().markOriginalDeletedByFilePath(photo.filePath, thumbPath)
        repository.getDao().markPendingPushCleared(listOf(itemId))

        // PC側から商品名変更などの更新が返ってきたJSONを再現（photos配列に新順やPC同期フラグが含まれる）
        val now = System.currentTimeMillis() + 5000L
        val serverArray = JSONArray().apply {
            put(
                JSONObject().apply {
                    put("id", itemId)
                    put("syncUuid", savedItem.item.syncUuid)
                    put("title", "PC側でタイトル更新済み")
                    put("description", "PC編集反映")
                    put("status", "LISTED")
                    put("price", 2800)
                    put("soldPrice", 0)
                    put("createdAt", savedItem.item.createdAt)
                    put("updatedAt", now)
                    put(
                        "photos",
                        JSONArray().apply {
                            put(
                                JSONObject().apply {
                                    put("md5Hash", origMd5)
                                    put("displayOrder", 0)
                                }
                            )
                        }
                    )
                }
            )
        }

        pcServerClient.mergeServerItemsFromJson(
            jsonArray = serverArray,
            permanentlyDeletedUuids = emptySet(),
            repairThumbnails = false
        )

        val merged = repository.getItemById(itemId)!!
        assertEquals("PC側でタイトル更新済み", merged.item.title)
        assertEquals(1, merged.photos.size)
        val mergedPhoto = merged.photos.first()
        assertEquals(origMd5, mergedPhoto.md5Hash)
        assertTrue("サムネイルのみ残存時はPull後も isOriginalDeleted=true を維持すべき", mergedPhoto.isOriginalDeleted)
        assertEquals(thumbPath, mergedPhoto.thumbnailPath)
        assertNotNull(mergedPhoto.displayFile)
    }
}
