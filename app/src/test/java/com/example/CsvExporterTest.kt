package com.example

import com.example.myapplication.data.CsvExporter
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.PhotoEntity
import org.junit.Assert.assertTrue
import org.junit.Test

class CsvExporterTest {

    @Test
    fun generateCsvContent_includesDescription_checksIsPcSynced_andEscapesMultiline() {
        val item1 = ItemEntity(
            id = 1,
            title = "テスト \"限定\" 商品",
            description = "1行目\n2行目 \"引用\"",
            status = "LISTED",
            price = 3000,
            soldPrice = 0,
            createdAt = 1700000000000L,
            updatedAt = 1700001000000L
        )
        val photo1 = PhotoEntity(
            id = 10,
            itemId = 1,
            filePath = "/tmp/p1.jpg",
            thumbnailPath = "/tmp/t1.jpg",
            isOriginalDeleted = false,
            isPcSynced = true,
            md5Hash = "abc123",
            syncStatus = "PENDING"
        )

        val item2 = ItemEntity(
            id = 2,
            title = "売却テスト",
            description = "簡易説明",
            status = "SOLD",
            price = 5000,
            soldPrice = 4800,
            shippingCost = 450,
            feeRatePercent = 10,
            expenseCost = 100,
            useCustomSettings = true,
            createdAt = 1700000000000L,
            updatedAt = 1700002000000L,
            soldAt = 1700002000000L
        )
        val photo2 = PhotoEntity(
            id = 11,
            itemId = 2,
            filePath = "/tmp/t2.jpg",
            thumbnailPath = "/tmp/t2.jpg",
            isOriginalDeleted = true,
            isPcSynced = true,
            md5Hash = "def456",
            syncStatus = "PENDING"
        )
        val photo3 = PhotoEntity(
            id = 12,
            itemId = 2,
            filePath = "/tmp/p3.jpg",
            thumbnailPath = "/tmp/t3.jpg",
            isOriginalDeleted = false,
            isPcSynced = false,
            md5Hash = "ghi789",
            syncStatus = "PENDING"
        )

        val csv = CsvExporter.generateCsvContent(
            listOf(
                ItemWithPhotos(item1, listOf(photo1)),
                ItemWithPhotos(item2, listOf(photo2, photo3))
            ),
            DefaultSettings()
        )

        assertTrue(csv.startsWith("\uFEFF"))
        assertTrue(csv.contains("ID,商品名,商品説明,ステータス,"))
        assertTrue(csv.contains("\"テスト \"\"限定\"\" 商品\""))
        assertTrue(csv.contains("\"1行目\n2行目 \"\"引用\"\"\""))
        assertTrue(csv.contains(",PC同期済,"))
        assertTrue(csv.contains(",一部同期,"))
        assertTrue(csv.contains(",4800,10,480,450,100,3770,"))
    }

    @Test
    fun generateCsvContent_phase64_statusLabels() {
        fun item(id: Int, status: String, soldPrice: Int = 0, soldAt: Long? = null) = ItemEntity(
            id = id,
            title = "状態テスト$id",
            description = "",
            status = status,
            price = 2000,
            soldPrice = soldPrice,
            createdAt = 1700000000000L,
            updatedAt = 1700001000000L,
            soldAt = soldAt
        )
        val csv = CsvExporter.generateCsvContent(
            listOf(
                ItemWithPhotos(item(11, "DRAFT"), emptyList()),
                ItemWithPhotos(item(12, "DRAFT_DONE"), emptyList()),
                ItemWithPhotos(item(13, "SHIPPING", soldPrice = 1900, soldAt = 1700002000000L), emptyList()),
                ItemWithPhotos(item(14, "SOME_FUTURE_CODE"), emptyList())
            ),
            DefaultSettings()
        )
        assertTrue(csv.contains(",下書き中,"))
        assertTrue(csv.contains(",下書き済み,"))
        assertTrue(csv.contains(",発送準備中,"))
        // 不明なコードは出品中へ倒さず、コードそのものがステータス欄に出ること
        assertTrue(csv.contains(",SOME_FUTURE_CODE,"))
    }
}
