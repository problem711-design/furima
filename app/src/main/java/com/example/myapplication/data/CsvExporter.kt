package com.example.myapplication.data

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import androidx.core.content.FileProvider
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemWithPhotos
import java.io.File
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object CsvExporter {

    fun generateCsvContent(
        itemsWithPhotos: List<ItemWithPhotos>,
        defaultSettings: DefaultSettings
    ): String {
        val sb = StringBuilder()
        // UTF-8 BOM for Microsoft Excel compatibility
        sb.append('\uFEFF')

        // Header
        sb.append("ID,商品名,ステータス,出品価格(円),実売却価格(円),手数料率(%),販売手数料(円),送料(円),梱包経費(円),想定/確定利益(円),写真枚数,同期ステータス,出品日,最終更新日,売却日,写真ハッシュ一覧,まとめ同梱内訳\n")

        val dateFormat = SimpleDateFormat("yyyy/MM/dd HH:mm", Locale.JAPAN)

        for (itemWithPhotos in itemsWithPhotos) {
            val item = itemWithPhotos.item
            val photos = itemWithPhotos.photos

            val statusText = when (item.status) {
                "SOLD" -> "売却済"
                "MERGED" -> "まとめ売却済"
                "DRAFT" -> "下書き"
                else -> "出品中"
            }

            val syncStatusText = if (photos.isNotEmpty() && photos.all { it.syncStatus == "SYNCED" || it.isOriginalDeleted }) {
                if (photos.any { it.isOriginalDeleted }) "サムネイル化済" else "PC同期済"
            } else if (photos.any { it.syncStatus == "SYNCED" }) {
                "一部同期"
            } else {
                "未送信"
            }

            val feePercent = item.getEffectiveFeeRatePercent(defaultSettings)
            val shipping = item.getEffectiveShippingCost(defaultSettings)
            val expense = item.getEffectiveExpenseCost(defaultSettings)
            val basePrice = if (item.status == "SOLD" && item.soldPrice > 0) item.soldPrice else item.price
            val fee = (basePrice * feePercent) / 100
            val profit = item.getCalculatedProfit(defaultSettings)

            val safeTitle = "\"" + item.title.replace("\"", "\"\"") + "\""
            val createdStr = dateFormat.format(Date(item.createdAt))
            val updatedStr = dateFormat.format(Date(item.updatedAt))
            val soldStr = item.soldAt?.let { dateFormat.format(Date(it)) } ?: ""
            val hashesStr = "\"" + photos.mapNotNull { it.md5Hash.takeIf { h -> h.isNotBlank() } }.joinToString(";") + "\""
            val mergedStr = "\"" + item.mergedChildTitles.replace("\"", "\"\"") + "\""

            sb.append("${item.id},$safeTitle,$statusText,${item.price},${item.soldPrice},$feePercent,$fee,$shipping,$expense,$profit,${photos.size},$syncStatusText,$createdStr,$updatedStr,$soldStr,$hashesStr,$mergedStr\n")
        }

        return sb.toString()
    }

    /**
     * CSVを端末の共有フォルダ（Downloads/FleaMarket）へ保存し、
     * 外部アプリ（Excel, スプレッドシート, ドライブ等）で開く／共有できるIntentを生成する
     */
    fun exportAndShareCsv(
        context: Context,
        itemsWithPhotos: List<ItemWithPhotos>,
        defaultSettings: DefaultSettings
    ): Intent? {
        try {
            val csvData = generateCsvContent(itemsWithPhotos, defaultSettings)
            val timeStamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.getDefault()).format(Date())
            val fileName = "flea_market_sales_$timeStamp.csv"

            // 1. ファイルプロバイダ用のキャッシュファイル作成
            val cacheDir = File(context.cacheDir, "exports")
            if (!cacheDir.exists()) cacheDir.mkdirs()
            val exportFile = File(cacheDir, fileName)
            FileOutputStream(exportFile).use { it.write(csvData.toByteArray(Charsets.UTF_8)) }

            // 2. 端末のダウンロード共有フォルダにも保存（外部ファイルマネージャーやPCからいつでも取り出し可能）
            saveToPublicDownloads(context, fileName, csvData)

            // 3. 共有Intent作成
            val authority = "${context.packageName}.fileprovider"
            val fileUri = FileProvider.getUriForFile(context, authority, exportFile)

            return Intent(Intent.ACTION_SEND).apply {
                type = "text/comma-separated-values"
                putExtra(Intent.EXTRA_SUBJECT, "フリマ売上・商品データ ($fileName)")
                putExtra(Intent.EXTRA_STREAM, fileUri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
        } catch (e: Exception) {
            e.printStackTrace()
            return null
        }
    }

    private fun saveToPublicDownloads(context: Context, fileName: String, csvContent: String) {
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                val values = ContentValues().apply {
                    put(MediaStore.Downloads.DISPLAY_NAME, fileName)
                    put(MediaStore.Downloads.MIME_TYPE, "text/csv")
                    put(MediaStore.Downloads.RELATIVE_PATH, "${Environment.DIRECTORY_DOWNLOADS}/FleaMarket")
                    put(MediaStore.Downloads.IS_PENDING, 1)
                }
                val uri = context.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
                if (uri != null) {
                    context.contentResolver.openOutputStream(uri)?.use {
                        it.write(csvContent.toByteArray(Charsets.UTF_8))
                    }
                    values.clear()
                    values.put(MediaStore.Downloads.IS_PENDING, 0)
                    context.contentResolver.update(uri, values, null, null)
                }
            } else {
                val downloadsDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
                val targetDir = File(downloadsDir, "FleaMarket").apply { mkdirs() }
                val targetFile = File(targetDir, fileName)
                FileOutputStream(targetFile).use { it.write(csvContent.toByteArray(Charsets.UTF_8)) }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }
}
