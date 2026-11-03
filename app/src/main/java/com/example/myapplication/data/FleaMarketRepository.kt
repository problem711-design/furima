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

class FleaMarketRepository(context: Context) {
    private val db = AppDatabase.getDatabase(context)
    private val dao = db.itemDao()
    private val photoStorageManager = PhotoStorageManager(context)
    private val defaultSettingsManager = DefaultSettingsManager(context)

    val defaultSettings: StateFlow<DefaultSettings> = defaultSettingsManager.settings

    fun updateDefaultSettings(newSettings: DefaultSettings) {
        defaultSettingsManager.updateSettings(newSettings)
    }

    fun getAllActiveItemsWithPhotos(): Flow<List<ItemWithPhotos>> = dao.getAllActiveItemsWithPhotos()

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
            if (existing != null) {
                newPhotoEntities.add(existing.copy(displayOrder = orderIndex++))
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
                newPhotoEntities.add(
                    PhotoEntity(
                        itemId = itemId,
                        filePath = savedResult.first,
                        thumbnailPath = savedResult.second,
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
     */
    suspend fun mergeItems(
        mergedTitle: String,
        mergedDescription: String,
        totalSoldPrice: Int,
        newShippingCost: Int,
        newFeeRatePercent: Int,
        newExpenseCost: Int,
        childItemIds: List<Int>
    ): Long {
        val childItems = dao.getItemsWithPhotosByIds(childItemIds)
        val childTitles = childItems.joinToString(", ") { "【${it.item.title}】¥${it.item.price}" }

        // 1. 新しいまとめ商品の作成
        val parentItem = ItemEntity(
            title = mergedTitle,
            description = mergedDescription,
            status = "SOLD", // まとめ買い成立
            price = totalSoldPrice,
            soldPrice = totalSoldPrice,
            shippingCost = newShippingCost,
            feeRatePercent = newFeeRatePercent,
            expenseCost = newExpenseCost,
            useCustomSettings = true,
            mergedChildCount = childItemIds.size,
            mergedChildTitles = childTitles,
            createdAt = System.currentTimeMillis(),
            soldAt = System.currentTimeMillis()
        )
        val parentId = dao.insertItem(parentItem).toInt()

        // 2. 元の個別商品のステータスを「MERGED」に更新（通常一覧から除外、履歴保持）
        dao.markItemsMerged(childItemIds, parentId)

        // 3. 元の各商品の写真をすべて親商品に引き継ぎ
        val parentPhotos = mutableListOf<PhotoEntity>()
        var order = 0
        for (child in childItems) {
            for (p in child.photos) {
                parentPhotos.add(p.copy(id = 0, itemId = parentId, displayOrder = order++))
            }
        }
        dao.insertPhotos(parentPhotos)

        return parentId.toLong()
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
}
