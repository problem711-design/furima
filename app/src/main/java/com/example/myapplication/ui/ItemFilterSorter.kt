package com.example.myapplication.ui

import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.SalesSummary
import com.example.myapplication.data.local.isSoldFamilyStatus
import java.util.Calendar

enum class SortOption(val label: String) {
    DATE_DESC("出品が新しい順"),
    DATE_ASC("出品が古い順"),
    SOLD_DATE_DESC("売却日が新しい順"),
    PRICE_DESC("価格が高い順"),
    PRICE_ASC("価格が安い順"),
    PROFIT_DESC("純利益が高い順")
}

enum class TimeRange(val label: String) {
    THIS_WEEK("今週"),
    THIS_MONTH("今月"),
    ALL_TIME("全期間")
}

data class GalleryExportResult(
    val savedCount: Int = 0,
    val fetchedFromServerCount: Int = 0,
    val fallbackLocalCount: Int = 0,
    val usedThumbnailOnlyCount: Int = 0
)

internal object ItemFilterSorter {
    fun matchesSearchQuery(text: String, query: String): Boolean {
        val q = query.trim()
        if (q.isBlank()) return true
        val lowerText = text.lowercase()

        // 1. 完全一致フレーズ検索: "..." で囲まれている場合
        if (q.startsWith("\"") && q.endsWith("\"") && q.length > 2) {
            val exactPhrase = q.substring(1, q.length - 1).lowercase()
            return lowerText.contains(exactPhrase)
        }

        // 2. OR 検索: 大文字の " OR " または " | " で区切られている場合
        if (q.contains(" OR ") || q.contains(" | ")) {
            val terms = q.split(Regex(" OR | \\| ")).map { it.trim().lowercase() }.filter { it.isNotBlank() }
            return terms.any { lowerText.contains(it) }
        }

        // 3. AND 検索: 半角または全角スペースで区切られた全単語を含む
        val words = q.split(Regex("[\\s　]+")).map { it.trim().lowercase() }.filter { it.isNotBlank() }
        return words.all { lowerText.contains(it) }
    }

    fun filterAndSortItems(
        items: List<ItemWithPhotos>,
        query: String,
        status: String,
        sort: SortOption,
        settings: DefaultSettings
    ): List<ItemWithPhotos> {
        return items.filter { itemWithPhotos ->
            val item = itemWithPhotos.item
            val itemFullText = "${item.title} ${item.description}"
            val matchesQuery = matchesSearchQuery(itemFullText, query)

            val matchesStatus = when (status) {
                "LISTED" -> item.status == "LISTED"
                "SOLD" -> item.status == "SOLD"
                "SHIPPING" -> item.status == "SHIPPING"
                "DRAFT" -> item.status == "DRAFT" || item.status == "DRAFT_DONE"
                else -> true
            }

            matchesQuery && matchesStatus
        }.sortedWith { a, b ->
            when (sort) {
                SortOption.DATE_DESC -> b.item.createdAt.compareTo(a.item.createdAt)
                SortOption.DATE_ASC -> a.item.createdAt.compareTo(b.item.createdAt)
                SortOption.SOLD_DATE_DESC -> (b.item.soldAt ?: 0L).compareTo(a.item.soldAt ?: 0L)
                SortOption.PRICE_DESC -> {
                    val pB = if (isSoldFamilyStatus(b.item.status) && b.item.soldPrice > 0) b.item.soldPrice else b.item.price
                    val pA = if (isSoldFamilyStatus(a.item.status) && a.item.soldPrice > 0) a.item.soldPrice else a.item.price
                    pB.compareTo(pA)
                }
                SortOption.PRICE_ASC -> {
                    val pB = if (isSoldFamilyStatus(b.item.status) && b.item.soldPrice > 0) b.item.soldPrice else b.item.price
                    val pA = if (isSoldFamilyStatus(a.item.status) && a.item.soldPrice > 0) a.item.soldPrice else a.item.price
                    pA.compareTo(pB)
                }
                SortOption.PROFIT_DESC -> {
                    b.item.getCalculatedProfit(settings).compareTo(a.item.getCalculatedProfit(settings))
                }
            }
        }
    }

    fun computePeriodSalesSummary(
        items: List<ItemWithPhotos>,
        settings: DefaultSettings,
        range: TimeRange,
        nowMillis: Long = System.currentTimeMillis()
    ): SalesSummary {
        val startOfPeriod = Calendar.getInstance().apply {
            timeInMillis = nowMillis
            when (range) {
                TimeRange.THIS_WEEK -> {
                    set(Calendar.DAY_OF_WEEK, firstDayOfWeek)
                    set(Calendar.HOUR_OF_DAY, 0)
                    set(Calendar.MINUTE, 0)
                    set(Calendar.SECOND, 0)
                    set(Calendar.MILLISECOND, 0)
                }
                TimeRange.THIS_MONTH -> {
                    set(Calendar.DAY_OF_MONTH, 1)
                    set(Calendar.HOUR_OF_DAY, 0)
                    set(Calendar.MINUTE, 0)
                    set(Calendar.SECOND, 0)
                    set(Calendar.MILLISECOND, 0)
                }
                TimeRange.ALL_TIME -> {
                    timeInMillis = 0L
                }
            }
        }.timeInMillis

        var totalSales = 0
        var totalProfit = 0
        var listedCount = 0
        var soldCount = 0

        for (itemWithPhotos in items) {
            val item = itemWithPhotos.item
            if (isSoldFamilyStatus(item.status)) {
                val targetTime = item.soldAt ?: item.createdAt
                if (targetTime >= startOfPeriod) {
                    soldCount++
                    val price = if (item.soldPrice > 0) item.soldPrice else item.price
                    totalSales += price
                    totalProfit += item.getCalculatedProfit(settings)
                }
            } else if (item.status == "LISTED") {
                if (item.createdAt >= startOfPeriod || range == TimeRange.ALL_TIME) {
                    listedCount++
                }
            }
        }

        return SalesSummary(totalSales, totalProfit, listedCount, soldCount)
    }

    fun buildMissingOriginalExportText(items: List<ItemWithPhotos>): String {
        val sb = StringBuilder()
        sb.append("【送信済み写真一覧 (サムネイル化済)】\n")
        for (itemWithPhotos in items) {
            sb.append("- 商品ID: ${itemWithPhotos.item.id} | タイトル: ${itemWithPhotos.item.title}\n")
            for (photo in itemWithPhotos.photos) {
                if (photo.isOriginalDeleted) {
                    sb.append("  写真ID: ${photo.id}, MD5: ${photo.md5Hash.ifBlank { "なし" }}\n")
                }
            }
        }
        return sb.toString()
    }
}
