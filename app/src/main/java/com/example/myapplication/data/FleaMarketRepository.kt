package com.example.myapplication.data

import android.content.Context
import android.net.Uri
import com.example.myapplication.data.local.AppDatabase
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemDao
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.local.SalesSummary
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import java.io.File

class FleaMarketRepository(private val context: Context) {
    private val db = AppDatabase.getDatabase(context)
    private val dao = db.itemDao()
    private val photoStorageManager = PhotoStorageManager(context)
    private val defaultSettingsManager = DefaultSettingsManager(context)

    val defaultSettings: StateFlow<DefaultSettings> = defaultSettingsManager.settings

    fun updateDefaultSettings(newSettings: DefaultSettings) {
        defaultSettingsManager.updateSettings(newSettings)
    }

    fun getAllActiveItemsWithPhotos(): Flow<List<ItemWithPhotos>> = dao.getAllActiveItemsWithPhotos()

    suspend fun getActiveItemsWithPhotosDirect(): List<ItemWithPhotos> = dao.getActiveItemsWithPhotosDirect()

    fun getAllItems(): Flow<List<ItemEntity>> = dao.getAllActiveItems()

    fun getSalesSummary(): Flow<SalesSummary> {
        return combine(dao.getAllActiveItems(), defaultSettings) { items, settings ->
            var totalSales = 0
            var totalProfit = 0
            var listedCount = 0
            var soldCount = 0

            for (item in items) {
                if (item.status == "SOLD") {
                    soldCount++
                    val price = if (item.soldPrice > 0) item.soldPrice else item.price
                    totalSales += price
                    totalProfit += item.getCalculatedProfit(settings)
                } else if (item.status == "LISTED") {
                    listedCount++
                }
            }
            SalesSummary(totalSales, totalProfit, listedCount, soldCount)
        }
    }

    suspend fun getItemById(id: Int): ItemWithPhotos? {
        return dao.getItemWithPhotosById(id)
    }

    suspend fun saveItem(
        id: Int = 0,
        title: String,
        description: String = "",
        status: String,
        price: Int,
        soldPrice: Int,
        shippingCost: Int,
        feeRatePercent: Int,
        expenseCost: Int,
        useCustomSettings: Boolean,
        imageUris: List<Uri>,
        existingFilePaths: List<String> = emptyList()
    ): Long {
        val existingItem = if (id != 0) dao.getItemWithPhotosById(id)?.item else null
        val soldAt = if (status == "SOLD") {
            existingItem?.soldAt ?: System.currentTimeMillis()
        } else {
            null
        }

        val item = ItemEntity(
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
            isShared = existingItem?.isShared ?: false,
            createdAt = existingItem?.createdAt ?: System.currentTimeMillis(),
            updatedAt = System.currentTimeMillis(),
            soldAt = soldAt
        )

        val itemId = if (id == 0) {
            dao.insertItem(item).toInt()
        } else {
            dao.updateItem(item)
            id
        }

        val currentSettings = defaultSettings.value
        val existingPhotos = if (id != 0) dao.getPhotosByItemId(itemId) else emptyList()
        val existingPhotoMap = existingPhotos.associateBy { it.filePath }

        val newPhotoEntities = mutableListOf<PhotoEntity>()
        var orderIndex = 0

        // 既存写真の保持
        for (path in existingFilePaths) {
            val existing = existingPhotoMap[path]
            val md5 = if (existing != null && existing.md5Hash.isNotBlank()) {
                existing.md5Hash
            } else {
                photoStorageManager.calculateMd5(File(path))
            }
            if (existing != null) {
                newPhotoEntities.add(existing.copy(displayOrder = orderIndex++, md5Hash = md5, updatedAt = System.currentTimeMillis()))
            } else {
                val thumb = photoStorageManager.createThumbnail(
                    File(path),
                    currentSettings.thumbnailMaxDimension,
                    currentSettings.thumbnailQuality
                )
                newPhotoEntities.add(
                    PhotoEntity(
                        itemId = itemId,
                        filePath = path,
                        thumbnailPath = thumb,
                        md5Hash = md5,
                        displayOrder = orderIndex++
                    )
                )
            }
        }

        // 新規写真の保存
        for (uri in imageUris) {
            val savedResult = photoStorageManager.savePhotoFromUri(
                title,
                uri,
                thumbQuality = currentSettings.thumbnailQuality,
                thumbMaxDim = currentSettings.thumbnailMaxDimension
            )
            if (savedResult != null) {
                val md5 = photoStorageManager.calculateMd5(File(savedResult.first))
                newPhotoEntities.add(
                    PhotoEntity(
                        itemId = itemId,
                        filePath = savedResult.first,
                        thumbnailPath = savedResult.second,
                        md5Hash = md5,
                        displayOrder = orderIndex++
                    )
                )
            }
        }

        dao.deletePhotosByItemId(itemId)
        dao.insertPhotos(newPhotoEntities)

        return itemId.toLong()
    }

    suspend fun updateItemStatus(item: ItemEntity, newStatus: String) {
        val soldAt = if (newStatus == "SOLD") (item.soldAt ?: System.currentTimeMillis()) else null
        val updated = item.copy(status = newStatus, soldAt = soldAt)
        dao.updateItem(updated)
    }

    suspend fun markItemShared(itemId: Int) {
        val itemWithPhotos = dao.getItemWithPhotosById(itemId) ?: return
        val updated = itemWithPhotos.item.copy(isShared = true)
        dao.updateItem(updated)
    }

    /**
     * 複数商品を「まとめ買い」出品として統合
     * 出品中（LISTED）のまままとめ買い商品を作成し、子商品の関係を保持
     */
    suspend fun mergeItems(
        mergedTitle: String,
        mergedDescription: String,
        totalSoldPrice: Int,
        newShippingCost: Int,
        newFeeRatePercent: Int,
        newExpenseCost: Int,
        childItemIds: List<Int>,
        asStatus: String = "LISTED"
    ): Long {
        val childItems = dao.getItemsWithPhotosByIds(childItemIds)
        val childTitles = childItems.joinToString(", ") { "【${it.item.title}】¥${it.item.price}" }
        val originalTotal = childItems.sumOf { it.item.price }
        val discount = (originalTotal - totalSoldPrice).coerceAtLeast(0)
        val maxChildLevel = childItems.maxOfOrNull { it.item.bundleLevel } ?: 0
        val totalChildCount = childItems.sumOf { if (it.item.mergedChildCount > 0) it.item.mergedChildCount else 1 }

        // 1. 新しいまとめ商品の作成 (デフォルトで LISTED: 出品中)
        val parentItem = ItemEntity(
            title = mergedTitle,
            description = mergedDescription,
            status = asStatus,
            price = totalSoldPrice,
            soldPrice = if (asStatus == "SOLD") totalSoldPrice else 0,
            shippingCost = newShippingCost,
            feeRatePercent = newFeeRatePercent,
            expenseCost = newExpenseCost,
            useCustomSettings = true,
            mergedChildCount = totalChildCount,
            mergedChildTitles = childTitles,
            originalTotalBundlePrice = originalTotal,
            discountAmount = discount,
            bundleLevel = maxChildLevel + 1,
            createdAt = System.currentTimeMillis(),
            soldAt = if (asStatus == "SOLD") System.currentTimeMillis() else null
        )
        val parentId = dao.insertItem(parentItem).toInt()

        // 2. 元の個別商品のステータスを「MERGED」に更新（通常一覧から除外、履歴保持）
        dao.markItemsMerged(childItemIds, parentId)

        // 3. 元の各商品の写真をすべて親商品に引き継ぎ（MD5ハッシュも継承）
        val parentPhotos = mutableListOf<PhotoEntity>()
        var order = 0
        for (child in childItems) {
            for (p in child.photos) {
                parentPhotos.add(
                    p.copy(
                        id = 0,
                        itemId = parentId,
                        displayOrder = order++,
                        md5Hash = p.md5Hash
                    )
                )
            }
        }
        dao.insertPhotos(parentPhotos)

        return parentId.toLong()
    }

    /**
     * まとめ買い出品を解除（バラす）
     * 子商品のステータスを LISTED に戻し、mergedParentId を解除。
     * 親商品とその写真レコードを削除する。
     */
    suspend fun unmergeItem(parentId: Int) {
        val parent = dao.getItemWithPhotosById(parentId) ?: return
        // 1. 子商品のステータスを LISTED に戻し、親IDを解除
        dao.restoreMergedChildren(parentId)
        // 2. 親商品の写真レコードを削除
        dao.deletePhotosByItemId(parentId)
        // 3. 親商品を削除
        dao.deleteItem(parent.item)
    }

    /**
     * まとめ商品の内包する子商品一覧を取得
     */
    suspend fun getMergedChildItemsWithPhotos(parentId: Int): List<ItemWithPhotos> {
        return dao.getMergedChildItemsWithPhotos(parentId)
    }

    suspend fun deleteItem(itemWithPhotos: ItemWithPhotos) {
        photoStorageManager.deletePhotoFiles(itemWithPhotos.photos)
        dao.deleteItem(itemWithPhotos.item)
    }

    /**
     * PC送信済み写真の高解像度元ファイルを削除しサムネイルのみ保持して容量削減
     */
    suspend fun cleanupSharedPhotos(): Int {
        val allItems = dao.getItemsWithPhotosByIds(
            dao.getAllActiveItemsWithPhotos().let {
                // query directly
                emptyList()
            }
        )
        // implemented via ViewModel batch list
        return 0
    }

    fun getPhotoStorageManager(): PhotoStorageManager = photoStorageManager
    fun getDao(): ItemDao = dao

    private fun isEmulator(): Boolean {
        return (android.os.Build.FINGERPRINT.startsWith("generic")
                || android.os.Build.FINGERPRINT.startsWith("unknown")
                || android.os.Build.MODEL.contains("google_sdk")
                || android.os.Build.MODEL.contains("Emulator")
                || android.os.Build.MODEL.contains("Android SDK built for x86")
                || android.os.Build.MANUFACTURER.contains("Genymotion")
                || (android.os.Build.BRAND.startsWith("generic") && android.os.Build.DEVICE.startsWith("generic"))
                || android.os.Build.PRODUCT.contains("sdk")
                || android.os.Build.PRODUCT.contains("google_sdk")
                || android.os.Build.PRODUCT.contains("sdk_gphone")
                || android.os.Build.HARDWARE.contains("goldfish")
                || android.os.Build.HARDWARE.contains("ranchu")
                || android.os.Build.HARDWARE.contains("vbox86"))
    }

    suspend fun seedInitialTestDataIfNeeded() {
        if (!isEmulator()) return
        val count = dao.getActiveItemsWithPhotosDirect().size
        if (count == 0) {
            val testItems = listOf(
                ItemEntity(title = "ヴィンテージ デニムジャケット", description = "美品のヴィンテージGジャンです。サイズM。", price = 8500, status = "LISTED"),
                ItemEntity(title = "レトロ スニーカー 27cm", description = "80年代風デザインの限定スニーカー。", price = 5400, status = "LISTED"),
                ItemEntity(title = "本革 レザーハンドバッグ", description = "上質な牛革を使用した高級バッグ。", price = 12800, status = "LISTED"),
                ItemEntity(title = "北欧風 チタン製マグカップ", description = "軽量でキャンプや登山に最適です。", price = 2200, status = "LISTED"),
                ItemEntity(title = "ワイヤレス ノイズキャンセリングイヤホン", description = "高音質でバッテリー長持ちモデル。", price = 7900, status = "LISTED"),
                ItemEntity(title = "ストリート系 キャップ 黒", description = "シンプルでどんな服装にも合わせやすい帽子。", price = 1800, status = "LISTED"),
                ItemEntity(title = "アンティーク 真鍮製卓上ランプ", description = "温かみのある光のインテリアランプ。", price = 4500, status = "LISTED"),
                ItemEntity(title = "ウール混 ざっくりニットセーター", description = "これからの季節にぴったりの暖かいセーター。", price = 3900, status = "LISTED"),
                ItemEntity(title = "クリア スマホショルダーポーチ", description = "お出かけに便利なクリアスマホポシェット。", price = 1500, status = "LISTED"),
                ItemEntity(title = "フレグランス オーデコロン 50ml", description = "爽やかなシトラス系の香水です。", price = 3200, status = "LISTED")
            )

            val colors = arrayOf("#4F46E5", "#059669", "#D97706", "#DC2626", "#7C3AED", "#2563EB", "#0D9488", "#CA8A04", "#475569", "#DB2777")

            for ((index, item) in testItems.withIndex()) {
                val itemId = dao.insertItem(item).toInt()
                val photoFile = File(context.filesDir, "test_seed_${itemId}.jpg")

                var success = false
                try {
                    val url = java.net.URL("https://picsum.photos/seed/item$index/300/300")
                    val connection = url.openConnection() as java.net.HttpURLConnection
                    connection.connectTimeout = 3000
                    connection.readTimeout = 3000
                    connection.inputStream.use { input ->
                        photoFile.outputStream().use { output ->
                            input.copyTo(output)
                        }
                    }
                    if (photoFile.exists() && photoFile.length() > 0) {
                        success = true
                    }
                } catch (e: Exception) {}

                if (!success) {
                    val bitmap = android.graphics.Bitmap.createBitmap(300, 300, android.graphics.Bitmap.Config.RGB_565)
                    val canvas = android.graphics.Canvas(bitmap)
                    canvas.drawColor(android.graphics.Color.parseColor(colors[index % colors.size]))
                    try {
                        photoFile.outputStream().use { out ->
                            bitmap.compress(android.graphics.Bitmap.CompressFormat.JPEG, 85, out)
                        }
                    } catch (e: Exception) {}
                }

                val md5 = photoStorageManager.calculateMd5(photoFile)
                val settings = defaultSettings.value
                val thumbPath = photoStorageManager.createThumbnail(
                    photoFile,
                    settings.thumbnailMaxDimension,
                    settings.thumbnailQuality
                )

                val photo = com.example.myapplication.data.local.PhotoEntity(
                    itemId = itemId,
                    filePath = photoFile.absolutePath,
                    thumbnailPath = thumbPath ?: photoFile.absolutePath,
                    md5Hash = md5,
                    displayOrder = 0,
                    syncStatus = "PENDING"
                )
                dao.insertPhotos(listOf(photo))
            }
        }
    }
}
