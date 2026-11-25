package com.example.myapplication.data

import com.example.myapplication.data.local.ItemDao
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.local.isSoldFamilyStatus

internal class TrashAndMergeRepository(
    private val dao: ItemDao,
    private val photoStorageManager: PhotoStorageManager,
    private val txRunner: DbTxRunner,
    private val addPendingDeletedUuid: (String) -> Unit
) {
    interface DbTxRunner {
        suspend fun <T> run(block: suspend () -> T): T
    }

    private suspend fun <T> withDbMutationTransaction(block: suspend () -> T): T = txRunner.run(block)

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
    ): Long = withDbMutationTransaction {
        val distinctIds = childItemIds.distinct()
        require(distinctIds.size >= 2) { "まとめ買いには2点以上の異なる商品が必要です" }
        require(asStatus in setOf("DRAFT", "DRAFT_DONE", "LISTED", "SHIPPING", "SOLD")) { "無効なステータスです: $asStatus" }
        val childItems = dao.getItemsWithPhotosByIds(distinctIds)
        require(childItems.size == distinctIds.size) { "対象の商品の一部が見つかりません" }
        for (c in childItems) {
            require(c.item.deletedAt == null) { "ゴミ箱内の商品はまとめられません: ${c.item.title}" }
            require(c.item.mergedParentId == null && c.item.status != "MERGED") { "既にまとめ済みの商品は選択できません: ${c.item.title}" }
            require(!isSoldFamilyStatus(c.item.status)) { "売却済み・発送待ちの商品はまとめられません: ${c.item.title}" }
        }
        val childTitles = childItems.joinToString(", ") { "【${it.item.title}】¥${it.item.price}" }
        val originalTotal = childItems.sumOf { it.item.price }
        val discount = (originalTotal - totalSoldPrice).coerceAtLeast(0)
        val maxChildLevel = childItems.maxOfOrNull { it.item.bundleLevel } ?: 0
        val totalChildCount = childItems.sumOf { if (it.item.mergedChildCount > 0) it.item.mergedChildCount else 1 }

        val now = System.currentTimeMillis()

        // 1. 新しいまとめ商品の作成 (デフォルトで LISTED 出品中)
        val parentItem = ItemEntity(
            title = mergedTitle,
            description = mergedDescription,
            status = asStatus,
            price = totalSoldPrice,
            soldPrice = if (isSoldFamilyStatus(asStatus)) totalSoldPrice else 0,
            shippingCost = newShippingCost,
            feeRatePercent = newFeeRatePercent,
            expenseCost = newExpenseCost,
            useCustomSettings = true,
            mergedChildCount = totalChildCount,
            mergedChildTitles = childTitles,
            originalTotalBundlePrice = originalTotal,
            discountAmount = discount,
            bundleLevel = maxChildLevel + 1,
            createdAt = now,
            updatedAt = now,
            soldAt = if (isSoldFamilyStatus(asStatus)) now else null,
            syncUuid = java.util.UUID.randomUUID().toString(),
            deletedAt = null,
            isDirty = true
        )
        val parentId = dao.insertItem(parentItem).toInt()

        // 2. 元の個別商品のステータスを「MERGED」に更新（通常一覧から除外、履歴保持）
        dao.markItemsMerged(distinctIds, parentId, now)

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

        parentId.toLong()
    }

    /**
     * まとめ買い出品を解除（バラす）
     * 子商品のステータスを LISTED に戻し、mergedParentId を解除。
     * 親商品とその写真レコードを削除し、親 syncUuid を完全削除記録へ追加する。
     */
    suspend fun unmergeItem(parentId: Int) {
        val deletedUuid = withDbMutationTransaction {
            val parent = dao.getItemWithPhotosById(parentId) ?: return@withDbMutationTransaction ""
            val now = System.currentTimeMillis()
            dao.restoreMergedChildren(parentId, now)
            dao.deletePhotosByItemId(parentId)
            dao.deleteItem(parent.item)
            parent.item.syncUuid
        }
        if (deletedUuid.isNotBlank()) {
            addPendingDeletedUuid(deletedUuid)
        }
    }

    /**
     * まとめ商品の内包する子商品一覧を取得
     */
    suspend fun getMergedChildItemsWithPhotos(parentId: Int): List<ItemWithPhotos> {
        return dao.getMergedChildItemsWithPhotos(parentId)
    }


    /**
     * 論理削除（ゴミ箱へ移動）：写真ファイルは保持し、30日間復元可能にする
     */
    suspend fun moveToTrash(itemWithPhotos: ItemWithPhotos) {
        withDbMutationTransaction {
            val now = System.currentTimeMillis()
            dao.softDeleteItem(itemWithPhotos.item.id, now, now)
            if (itemWithPhotos.item.mergedChildCount > 0) {
                dao.softDeleteChildrenByParent(itemWithPhotos.item.id, now, now)
            }
        }
    }

    /**
     * 既存呼び出し互換のための削除エントリ（論理削除を実行）
     */
    suspend fun deleteItem(itemWithPhotos: ItemWithPhotos) {
        moveToTrash(itemWithPhotos)
    }


    /**
     * ゴミ箱から元の出品状態へ復元
     */
    suspend fun restoreFromTrash(itemId: Int) {
        withDbMutationTransaction {
            val now = System.currentTimeMillis()
            dao.restoreFromTrash(itemId, now)
            dao.restoreChildrenByParent(itemId, now)
        }
    }

    suspend fun moveItemsToTrash(itemIds: List<Int>): Int {
        var count = 0
        for (id in itemIds.distinct()) {
            val itemWithPhotos = dao.getItemWithPhotosById(id) ?: continue
            moveToTrash(itemWithPhotos)
            count++
        }
        return count
    }

    suspend fun restoreItemsFromTrash(itemIds: List<Int>): Int {
        var count = 0
        for (id in itemIds.distinct()) {
            restoreFromTrash(id)
            count++
        }
        return count
    }

    suspend fun permanentlyDeleteItemsFromTrash(itemIds: List<Int>): Int {
        var count = 0
        for (id in itemIds.distinct()) {
            val itemWithPhotos = dao.getItemWithPhotosById(id) ?: continue
            permanentlyDeleteItem(itemWithPhotos, recordTombstoneForSync = true)
            count++
        }
        return count
    }

    suspend fun batchUpdateItems(itemIds: List<Int>, spec: BatchEditSpec): Int {
        return withDbMutationTransaction {
            val now = System.currentTimeMillis()
            var updatedCount = 0
            for (id in itemIds.distinct()) {
                val existing = dao.getItemWithPhotosById(id)?.item ?: continue
                if (existing.deletedAt != null || existing.status == "MERGED") continue

                val nextPrice = when (spec.priceMode) {
                    BatchPriceMode.NONE -> existing.price
                    BatchPriceMode.FIXED -> spec.priceValue.coerceAtLeast(0)
                    BatchPriceMode.DELTA_YEN -> (existing.price + spec.priceValue).coerceAtLeast(0)
                    BatchPriceMode.DELTA_PERCENT -> (existing.price + (existing.price * spec.priceValue) / 100).coerceAtLeast(0)
                }

                val nextStatus = spec.newStatus ?: existing.status
                val nextSoldAt = if (isSoldFamilyStatus(nextStatus)) (existing.soldAt ?: now) else null
                val nextSoldPrice = when {
                    !isSoldFamilyStatus(nextStatus) -> 0
                    spec.priceMode != BatchPriceMode.NONE -> nextPrice
                    existing.soldPrice > 0 -> existing.soldPrice
                    else -> nextPrice
                }

                val updated = existing.copy(
                    status = nextStatus,
                    price = nextPrice,
                    soldPrice = nextSoldPrice,
                    soldAt = nextSoldAt,
                    useCustomSettings = if (spec.applyCostPreset) spec.useCustomSettings else existing.useCustomSettings,
                    feeRatePercent = if (spec.applyCostPreset) spec.feeRatePercent.coerceAtLeast(0) else existing.feeRatePercent,
                    shippingCost = if (spec.applyCostPreset) spec.shippingCost.coerceAtLeast(0) else existing.shippingCost,
                    expenseCost = if (spec.applyCostPreset) spec.expenseCost.coerceAtLeast(0) else existing.expenseCost,
                    updatedAt = now,
                    isDirty = true
                )
                dao.updateItem(updated)
                updatedCount++
            }
            updatedCount
        }
    }

    /**
     * 完全削除：他アイテムから参照されていない写真ファイルとDBレコードを物理削除する
     */
    suspend fun permanentlyDeleteItem(
        itemWithPhotos: ItemWithPhotos,
        recordTombstoneForSync: Boolean = true
    ) {
        val item = itemWithPhotos.item
        if (item.mergedChildCount > 0) {
            val children = dao.getMergedChildItemsWithPhotos(item.id)
            for (child in children) {
                deleteSingleItemPhysically(child, recordTombstoneForSync)
            }
        }
        deleteSingleItemPhysically(itemWithPhotos, recordTombstoneForSync)
    }

    private suspend fun deleteSingleItemPhysically(
        itemWithPhotos: ItemWithPhotos,
        recordTombstoneForSync: Boolean
    ) {
        val item = itemWithPhotos.item
        val unreferencedPhotos = withDbMutationTransaction {
            val fresh = dao.getItemWithPhotosById(item.id)
            val targetItem = fresh?.item ?: item
            val targetPhotos = fresh?.photos ?: itemWithPhotos.photos
            val otherPhotos = dao.getPhotosExcludingItem(targetItem.id)
            val referencedPaths = HashSet<String>()
            for (p in otherPhotos) {
                if (p.filePath.isNotBlank()) referencedPaths.add(p.filePath)
                if (!p.thumbnailPath.isNullOrBlank()) referencedPaths.add(p.thumbnailPath)
            }

            val toDelete = targetPhotos.mapNotNull { p ->
                val origOk = p.filePath.isNotBlank() && !referencedPaths.contains(p.filePath)
                val thumbOk = !p.thumbnailPath.isNullOrBlank() && !referencedPaths.contains(p.thumbnailPath)
                if (!origOk && !thumbOk) {
                    null
                } else {
                    p.copy(
                        filePath = if (origOk) p.filePath else "",
                        thumbnailPath = if (thumbOk) p.thumbnailPath else null
                    )
                }
            }
            dao.deletePhotosByItemId(targetItem.id)
            dao.deleteItem(targetItem)
            toDelete
        }
        if (unreferencedPhotos.isNotEmpty()) {
            photoStorageManager.deletePhotoFiles(unreferencedPhotos)
        }
        if (recordTombstoneForSync && item.syncUuid.isNotBlank()) {
            addPendingDeletedUuid(item.syncUuid)
        }
    }

    /**
     * ゴミ箱を空にする（現在ゴミ箱にある全商品を完全削除）
     */
    suspend fun emptyTrash(): Int {
        val trashed = dao.getTrashedItemsWithPhotosDirect()
        for (item in trashed) {
            permanentlyDeleteItem(item, recordTombstoneForSync = true)
        }
        return trashed.size
    }

    /**
     * ゴミ箱投入から保持日数を超えた商品を自動完全削除
     */
    suspend fun purgeExpiredTrash(retentionDays: Int = 30): Int {
        val cutoffMs = System.currentTimeMillis() - (retentionDays * 24L * 60L * 60L * 1000L)
        val expired = dao.getExpiredTrashedItemsDirect(cutoffMs)
        if (expired.isEmpty()) return 0

        var purgedCount = 0
        for (itemWithPhotos in expired.filter { it.item.mergedParentId == null }) {
            if (dao.getItemWithPhotosById(itemWithPhotos.item.id) != null) {
                permanentlyDeleteItem(itemWithPhotos, recordTombstoneForSync = true)
                purgedCount++
            }
        }
        for (itemWithPhotos in expired.filter { it.item.mergedParentId != null }) {
            if (dao.getItemWithPhotosById(itemWithPhotos.item.id) != null) {
                permanentlyDeleteItem(itemWithPhotos, recordTombstoneForSync = true)
                purgedCount++
            }
        }
        return purgedCount
    }

}
