package com.example

import android.content.Context
import android.graphics.Bitmap
import android.net.Uri
import androidx.test.core.app.ApplicationProvider
import com.example.myapplication.data.DefaultSettingsManager
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.AppDatabase
import com.example.myapplication.data.local.TextTemplate
import com.example.myapplication.data.server.PcServerClient
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.UUID

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [36])
class Phase214TrashAndSyncTest {

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
        if (photosDir.exists()) {
            photosDir.deleteRecursively()
        }
        val thumbDir = File(context.filesDir, "Thumbnails")
        if (thumbDir.exists()) {
            thumbDir.deleteRecursively()
        }
    }

    private fun createTempJpegFile(name: String): File {
        val dir = File(context.cacheDir, "phase214_test_images").apply { mkdirs() }
        val file = File(dir, name)
        val bmp = Bitmap.createBitmap(120, 120, Bitmap.Config.ARGB_8888)
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, 90, out)
        bmp.recycle()
        file.writeBytes(out.toByteArray())
        createdTempFiles.add(file)
        return file
    }

    @Test
    fun `trash move preserves photo file, restore brings item back, and permanent delete removes file and adds tombstone`() = runBlocking {
        val tempPhoto = createTempJpegFile("sample_item.jpg")
        val itemId = repository.saveItem(
            title = "テスト出品商品",
            description = "ゴミ箱検証用",
            status = "LISTED",
            price = 1800,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = listOf(Uri.fromFile(tempPhoto))
        ).toInt()

        val saved = repository.getItemById(itemId)
        assertNotNull(saved)
        val savedItem = saved!!
        assertEquals(1, savedItem.photos.size)
        val storedPhotoPath = savedItem.photos.first().filePath
        assertTrue(storedPhotoPath.isNotBlank())
        assertTrue(File(storedPhotoPath).exists())
        val itemSyncUuid = savedItem.item.syncUuid
        assertTrue(itemSyncUuid.isNotBlank())

        // 1. 論理削除 (ゴミ箱へ移動) 時点では通常一覧から除外され、写真ファイルは残る
        repository.moveToTrash(savedItem)
        val activeAfterTrash = repository.getActiveItemsWithPhotosDirect()
        assertTrue(activeAfterTrash.none { it.item.id == itemId })
        val trashedList = repository.getTrashedItemsWithPhotos().first()
        assertEquals(1, trashedList.size)
        assertEquals(itemId, trashedList.first().item.id)
        assertNotNull(trashedList.first().item.deletedAt)
        assertTrue(File(storedPhotoPath).exists())

        // 2. ゴミ箱から復元すると通常一覧へ戻り、写真パスも保持される
        repository.restoreFromTrash(itemId)
        val activeAfterRestore = repository.getActiveItemsWithPhotosDirect()
        assertEquals(1, activeAfterRestore.size)
        assertNull(activeAfterRestore.first().item.deletedAt)
        assertEquals(storedPhotoPath, activeAfterRestore.first().photos.first().filePath)
        assertTrue(File(storedPhotoPath).exists())
        assertTrue(repository.getTrashedItemsWithPhotos().first().isEmpty())

        // 3. 完全削除で DB レコードと写真ファイルが削除され、syncUuid が tombstone に追加される
        val toPurge = repository.getItemById(itemId)!!
        repository.permanentlyDeleteItem(toPurge, recordTombstoneForSync = true)
        assertNull(repository.getItemById(itemId))
        assertFalse(File(storedPhotoPath).exists())
        assertTrue(repository.getPendingDeletedUuids().contains(itemSyncUuid))
    }

    @Test
    fun `dirty local update is not overwritten by older server item json`() = runBlocking {
        val localItemId = repository.saveItem(
            title = "ローカル最新編集タイトル",
            description = "未送信変更あり",
            status = "LISTED",
            price = 3200,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = emptyList()
        ).toInt()

        val localSaved = repository.getItemById(localItemId)!!.item
        assertTrue(localSaved.isDirty)

        // サーバー側の同じ syncUuid だが updatedAt が古い JSON を作成
        val olderTimestamp = localSaved.updatedAt - 60_000L
        val serverArray = JSONArray().apply {
            put(
                JSONObject().apply {
                    put("id", localItemId)
                    put("syncUuid", localSaved.syncUuid)
                    put("title", "サーバー上の古いタイトル")
                    put("description", "古い説明")
                    put("status", "DRAFT")
                    put("price", 1500)
                    put("soldPrice", 0)
                    put("updatedAt", olderTimestamp)
                    put("createdAt", localSaved.createdAt)
                    put("photos", JSONArray())
                }
            )
        }

        pcServerClient.mergeServerItemsFromJson(
            jsonArray = serverArray,
            permanentlyDeletedUuids = emptySet(),
            repairThumbnails = false
        )

        val afterMerge = repository.getItemById(localItemId)!!.item
        assertEquals("ローカル最新編集タイトル", afterMerge.title)
        assertEquals(3200, afterMerge.price)
        assertEquals("LISTED", afterMerge.status)
        assertTrue(afterMerge.isDirty)
    }

    @Test
    fun `different syncUuid with colliding numeric id preserves both items as distinct records`() = runBlocking {
        val localId = repository.saveItem(
            title = "スマホ側商品 (ID=1相当)",
            description = "ローカル作成",
            status = "LISTED",
            price = 1200,
            soldPrice = 0,
            shippingCost = 210,
            feeRatePercent = 5,
            expenseCost = 55,
            useCustomSettings = false,
            imageUris = emptyList()
        ).toInt()

        val localItem = repository.getItemById(localId)!!.item
        val serverOnlyUuid = UUID.randomUUID().toString()
        val now = System.currentTimeMillis()

        // スマホ側と同じ数値 id だが異なる syncUuid を持つPC作成商品をマージ
        val serverArray = JSONArray().apply {
            put(
                JSONObject().apply {
                    put("id", localId)
                    put("syncUuid", serverOnlyUuid)
                    put("title", "PC側新規作成商品 (同じID番号衝突)")
                    put("description", "PCから到着")
                    put("status", "LISTED")
                    put("price", 4500)
                    put("soldPrice", 0)
                    put("createdAt", now)
                    put("updatedAt", now)
                    put("photos", JSONArray())
                }
            )
        }

        pcServerClient.mergeServerItemsFromJson(
            jsonArray = serverArray,
            permanentlyDeletedUuids = emptySet(),
            repairThumbnails = false
        )

        val allActive = repository.getActiveItemsWithPhotosDirect()
        assertEquals(2, allActive.size)

        val preservedLocal = allActive.firstOrNull { it.item.syncUuid == localItem.syncUuid }
        val importedServer = allActive.firstOrNull { it.item.syncUuid == serverOnlyUuid }

        assertNotNull(preservedLocal)
        assertNotNull(importedServer)
        assertEquals("スマホ側商品 (ID=1相当)", preservedLocal!!.item.title)
        assertEquals("PC側新規作成商品 (同じID番号衝突)", importedServer!!.item.title)
        assertTrue(preservedLocal.item.id != importedServer.item.id)
    }

    @Test
    fun `default settings title and description templates persist across json save and reload`() {
        val manager = DefaultSettingsManager(context)
        val initial = manager.settings.value

        val newTitleTemplates = listOf(
            TextTemplate(id = "t1", name = "美品アピール", content = "【美品】"),
            TextTemplate(id = "t2", name = "匿名配送", content = " 匿名配送")
        )
        val newDescTemplates = listOf(
            TextTemplate(id = "d1", name = "ペット喫煙なし", content = "ペット・喫煙者はおりません。即購入OKです。"),
            TextTemplate(id = "d2", name = "梱包案内", content = "防水対策の上、緩衝材で梱包して発送します。")
        )

        val updated = initial.copy(
            defaultFeeRatePercent = 10,
            defaultShippingCost = 450,
            defaultExpenseCost = 80,
            titleTemplates = newTitleTemplates,
            descriptionTemplates = newDescTemplates
        )
        manager.updateSettings(updated, stampNow = true)

        // 新しい DefaultSettingsManager インスタンスから再ロードしてもテンプレートと日時が失われないことを確認
        val reloadedManager = DefaultSettingsManager(context)
        val reloaded = reloadedManager.settings.value

        assertEquals(10, reloaded.defaultFeeRatePercent)
        assertEquals(450, reloaded.defaultShippingCost)
        assertEquals(80, reloaded.defaultExpenseCost)
        assertTrue(reloaded.settingsUpdatedAt > 0L)
        assertEquals(newTitleTemplates, reloaded.titleTemplates)
        assertEquals(newDescTemplates, reloaded.descriptionTemplates)

        // テンプレート追加更新も正しく反映・保持されることを確認
        val appendedTitle = reloaded.titleTemplates + TextTemplate(id = "t3", name = "値下げ済", content = "【最終値下げ】")
        reloadedManager.updateSettings(reloaded.copy(titleTemplates = appendedTitle), stampNow = true)

        val thirdLoad = DefaultSettingsManager(context).settings.value
        assertEquals(3, thirdLoad.titleTemplates.size)
        assertEquals("【最終値下げ】", thirdLoad.titleTemplates.last().content)
    }
}
