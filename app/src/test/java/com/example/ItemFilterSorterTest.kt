package com.example

import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.ui.ItemFilterSorter
import com.example.myapplication.ui.SortOption
import com.example.myapplication.ui.TimeRange
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ItemFilterSorterTest {

    private fun item(
        id: Int,
        title: String,
        description: String = "",
        status: String = "LISTED",
        price: Int = 1000,
        soldPrice: Int = 0,
        shippingCost: Int = 210,
        feeRatePercent: Int = 5,
        expenseCost: Int = 55,
        useCustomSettings: Boolean = false,
        createdAt: Long = 1000L * id,
        soldAt: Long? = null
    ): ItemWithPhotos = ItemWithPhotos(
        item = ItemEntity(
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
            createdAt = createdAt,
            soldAt = soldAt
        ),
        photos = emptyList()
    )

    @Test
    fun `matchesSearchQuery supports blank exact OR and mixed space AND queries`() {
        val text = "Nintendo Switch 有機EL 本体 美品"
        assertTrue(ItemFilterSorter.matchesSearchQuery(text, "   "))
        assertTrue(ItemFilterSorter.matchesSearchQuery(text, "\"Switch 有機EL\""))
        assertFalse(ItemFilterSorter.matchesSearchQuery(text, "\"Switch Lite\""))
        assertTrue(ItemFilterSorter.matchesSearchQuery(text, "PS5 OR switch"))
        assertTrue(ItemFilterSorter.matchesSearchQuery(text, "PS5 | 本体"))
        assertFalse(ItemFilterSorter.matchesSearchQuery(text, "PS5 OR Xbox"))
        assertTrue(ItemFilterSorter.matchesSearchQuery(text, "nintendo　有機el 美品"))
        assertFalse(ItemFilterSorter.matchesSearchQuery(text, "nintendo ジャンク"))
    }

    @Test
    fun `filterAndSortItems recalculates PROFIT_DESC when DefaultSettings changes`() {
        val highFeeCustom = item(
            id = 1,
            title = "Custom High Shipping",
            price = 2000,
            shippingCost = 850,
            feeRatePercent = 10,
            expenseCost = 100,
            useCustomSettings = true
        )
        val defaultItem = item(
            id = 2,
            title = "Default Item",
            price = 2000,
            useCustomSettings = false
        )

        val lowDefaultFee = DefaultSettings(defaultFeeRatePercent = 5, defaultShippingCost = 160, defaultExpenseCost = 20)
        val sortedWithLowFee = ItemFilterSorter.filterAndSortItems(
            listOf(highFeeCustom, defaultItem),
            query = "",
            status = "ALL",
            sort = SortOption.PROFIT_DESC,
            settings = lowDefaultFee
        )
        assertEquals(listOf(2, 1), sortedWithLowFee.map { it.item.id })

        val hugeDefaultFee = DefaultSettings(defaultFeeRatePercent = 50, defaultShippingCost = 750, defaultExpenseCost = 200)
        val sortedWithHighFee = ItemFilterSorter.filterAndSortItems(
            listOf(highFeeCustom, defaultItem),
            query = "",
            status = "ALL",
            sort = SortOption.PROFIT_DESC,
            settings = hugeDefaultFee
        )
        assertEquals(listOf(1, 2), sortedWithHighFee.map { it.item.id })
    }

    @Test
    fun `filterAndSortItems handles DRAFT_DONE under DRAFT filter and soldPrice in PRICE_DESC`() {
        val draft = item(id = 1, title = "下書き", status = "DRAFT", price = 500)
        val draftDone = item(id = 2, title = "撮影済み", status = "DRAFT_DONE", price = 900)
        val listed = item(id = 3, title = "出品品", status = "LISTED", price = 1500)
        val soldHigher = item(id = 4, title = "売却品", status = "SOLD", price = 1000, soldPrice = 2200)

        val settings = DefaultSettings()
        val draftOnly = ItemFilterSorter.filterAndSortItems(
            listOf(draft, draftDone, listed, soldHigher),
            query = "",
            status = "DRAFT",
            sort = SortOption.DATE_ASC,
            settings = settings
        )
        assertEquals(listOf(1, 2), draftOnly.map { it.item.id })

        val byPriceDesc = ItemFilterSorter.filterAndSortItems(
            listOf(draft, listed, soldHigher),
            query = "",
            status = "ALL",
            sort = SortOption.PRICE_DESC,
            settings = settings
        )
        assertEquals(listOf(4, 3, 1), byPriceDesc.map { it.item.id })
    }

    @Test
    fun `computePeriodSalesSummary counts SOLD and SHIPPING family items`() {
        val now = 1_700_000_000_000L
        val settings = DefaultSettings(defaultFeeRatePercent = 10, defaultShippingCost = 200, defaultExpenseCost = 50)
        val sold = item(id = 1, title = "A", status = "SOLD", price = 1000, soldPrice = 1200, soldAt = now)
        val shipping = item(id = 2, title = "B", status = "SHIPPING", price = 1000, soldPrice = 1000, soldAt = now)
        val listed = item(id = 3, title = "C", status = "LISTED", price = 500, createdAt = now)

        val summary = ItemFilterSorter.computePeriodSalesSummary(
            listOf(sold, shipping, listed),
            settings = settings,
            range = TimeRange.ALL_TIME,
            nowMillis = now
        )
        assertEquals(2, summary.soldCount)
        assertEquals(1, summary.listedCount)
        assertEquals(2200, summary.totalSales)
    }
}
