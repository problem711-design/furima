package com.example.myapplication.ui

import android.app.Application
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.widget.Toast
import com.example.myapplication.data.CsvExporter
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.data.server.PcServerClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

internal class MaintenanceExportHelper(
    private val application: Application,
    private val repository: FleaMarketRepository,
    private val pcServerClient: PcServerClient,
    private val pcServerUrl: () -> String,
    private val pcAuthToken: () -> String,
    private val isPcServerConnected: () -> Boolean
) {
    fun findCachedOriginalPhotoFile(md5Hash: String): File? {
        val cleanHash = md5Hash.trim().lowercase()
        if (cleanHash.isBlank()) return null
        val cacheDir = File(application.cacheDir, "preview_originals")
        val target = File(cacheDir, "$cleanHash.jpg")
        return target.takeIf { it.exists() && it.length() > 0L }
    }

    suspend fun fetchOriginalPhotoCacheFile(md5Hash: String): File? = withContext(Dispatchers.IO) {
        val cleanHash = md5Hash.trim().lowercase()
        if (cleanHash.isBlank()) return@withContext null
        val existing = findCachedOriginalPhotoFile(cleanHash)
        if (existing != null) return@withContext existing

        val url = pcServerUrl()
        val token = pcAuthToken()
        if (url.isBlank() || !isPcServerConnected()) return@withContext null

        val cacheDir = File(application.cacheDir, "preview_originals").apply { mkdirs() }
        val outFile = File(cacheDir, "$cleanHash.jpg")
        val ok = pcServerClient.downloadPhotoToFileByHash(url, token, cleanHash, outFile, verifyMd5 = true)
        if (ok) outFile.takeIf { it.exists() && it.length() > 0L } else null
    }

    suspend fun createZipShareIntent(itemWithPhotos: ItemWithPhotos): Intent? = withContext(Dispatchers.IO) {
        val item = itemWithPhotos.item
        val photos = itemWithPhotos.photos.mapNotNull { it.displayFile?.absolutePath }
        if (photos.isEmpty() && item.description.isBlank()) return@withContext null
        repository.getPhotoStorageManager().createStoredZipForSharing(
            itemTitle = item.title,
            description = item.description,
            photoPaths = photos
        )
    }

    suspend fun exportSelectedPhotosToGallery(
        itemTitle: String,
        selectedPhotos: List<PhotoEntity>,
        fetchOriginalsFromPc: Boolean
    ): GalleryExportResult = withContext(Dispatchers.IO) {
        val storageManager = repository.getPhotoStorageManager()
        val url = pcServerUrl()
        val token = pcAuthToken()
        val isOnline = isPcServerConnected() && url.isNotBlank()

        var saved = 0
        var fetchedFromServer = 0
        var fallbackLocal = 0
        var thumbOnly = 0

        selectedPhotos.forEachIndexed { index, photo ->
            val origFile = if (!photo.isOriginalDeleted && photo.filePath.isNotBlank()) {
                File(photo.filePath).takeIf {
                    it.exists() && !it.absolutePath.equals(photo.thumbnailPath, ignoreCase = true)
                }
            } else null

            if (fetchOriginalsFromPc && isOnline) {
                if (origFile != null) {
                    if (storageManager.syncFileToPublicGallery(itemTitle, origFile) != null) {
                        saved++
                    }
                } else if (photo.md5Hash.isNotBlank()) {
                    val bytes = pcServerClient.downloadPhotoByHash(url, token, photo.md5Hash, verifyMd5 = true)
                    if (bytes != null) {
                        val fileName = "ORIG_${index + 1}_${photo.md5Hash.take(8)}.jpg"
                        if (storageManager.saveBytesToPublicGallery(itemTitle, fileName, bytes) != null) {
                            saved++
                            fetchedFromServer++
                            return@forEachIndexed
                        }
                    }
                    val fallbackFile = photo.displayFile
                    if (fallbackFile != null && storageManager.syncFileToPublicGallery(itemTitle, fallbackFile) != null) {
                        saved++
                        fallbackLocal++
                        thumbOnly++
                    }
                } else {
                    val fallbackFile = photo.displayFile
                    if (fallbackFile != null && storageManager.syncFileToPublicGallery(itemTitle, fallbackFile) != null) {
                        saved++
                        fallbackLocal++
                        if (photo.isOriginalDeleted || fallbackFile.absolutePath == photo.thumbnailPath) {
                            thumbOnly++
                        }
                    }
                }
            } else {
                val targetFile = origFile ?: photo.displayFile
                if (targetFile != null && storageManager.syncFileToPublicGallery(itemTitle, targetFile) != null) {
                    saved++
                    if (origFile == null) {
                        thumbOnly++
                    }
                }
            }
        }

        GalleryExportResult(
            savedCount = saved,
            fetchedFromServerCount = fetchedFromServer,
            fallbackLocalCount = fallbackLocal,
            usedThumbnailOnlyCount = thumbOnly
        )
    }

    suspend fun exportItemPhotosToGallery(itemTitle: String, photoPaths: List<String>): Int = withContext(Dispatchers.IO) {
        repository.getPhotoStorageManager().exportPhotosToGallery(itemTitle, photoPaths)
    }

    suspend fun exportAllToCsvIntent(
        context: Context,
        items: List<ItemWithPhotos>,
        settings: DefaultSettings
    ): Intent? = withContext(Dispatchers.IO) {
        CsvExporter.exportAndShareCsv(context, items, settings)
    }

    fun copyListingTemplate(context: Context, itemWithPhotos: ItemWithPhotos) {
        val item = itemWithPhotos.item
        val text = buildString {
            appendLine("【商品名】${item.title}")
            appendLine("【価格】¥${item.price}")
            if (item.description.isNotBlank()) {
                appendLine("")
                appendLine("【商品説明】")
                appendLine(item.description)
            }
            appendLine("")
            appendLine("ご覧いただきありがとうございます。即購入OKです。")
            appendLine("丁寧な梱包と迅速な発送を心がけております。よろしくお願いいたします。")
        }
        val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val clip = ClipData.newPlainText("出品テキスト", text)
        clipboard.setPrimaryClip(clip)
        Toast.makeText(context, "出品用テキストをクリップボードにコピーしました", Toast.LENGTH_SHORT).show()
    }

    suspend fun cleanupSharedOriginalPhotos(allItems: List<ItemWithPhotos>): Int = withContext(Dispatchers.IO) {
        val items = allItems.filter { it.item.isShared && !it.item.isDirty }
        var totalDeleted = 0
        val storageManager = repository.getPhotoStorageManager()
        val dao = repository.getDao()
        val deletedOrigPaths = mutableSetOf<String>()
        for (itemWithPhotos in items) {
            if (repository.isItemEditingActive(itemWithPhotos.item.id)) continue
            for (photo in itemWithPhotos.photos) {
                val thumbPath = photo.thumbnailPath ?: continue
                if (!File(thumbPath).exists()) continue
                if (photo.filePath.isBlank() || photo.filePath.equals(thumbPath, ignoreCase = true)) continue
                if (repository.isFilePathProtectedByEditingOrDirty(photo.filePath)) continue
                val origFile = File(photo.filePath)
                if (origFile.exists()) {
                    var resolvedMd5 = photo.md5Hash
                    if (resolvedMd5.isBlank()) {
                        resolvedMd5 = storageManager.calculateMd5(origFile)
                    }
                    repository.withDbMutationLock {
                        if (repository.isFilePathProtectedByEditingOrDirty(photo.filePath)) {
                            return@withDbMutationLock
                        }
                        if (photo.md5Hash.isBlank() && resolvedMd5.isNotBlank()) {
                            dao.updatePhotoMd5Hash(photo.id, resolvedMd5)
                        }
                        if (deletedOrigPaths.contains(photo.filePath)) {
                            dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                        } else if (origFile.exists() && origFile.delete()) {
                            deletedOrigPaths.add(photo.filePath)
                            totalDeleted++
                            dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                        }
                    }
                } else if (!photo.isOriginalDeleted) {
                    repository.withDbMutationLock {
                        if (!repository.isFilePathProtectedByEditingOrDirty(photo.filePath)) {
                            dao.markOriginalDeletedByFilePath(photo.filePath, thumbPath)
                        }
                    }
                }
            }
        }
        totalDeleted
    }

    suspend fun cleanupSoldPhotosOlderThan(allItems: List<ItemWithPhotos>, days: Int): Int = withContext(Dispatchers.IO) {
        val thresholdTime = System.currentTimeMillis() - (days * 24L * 60L * 60L * 1000L)
        val soldItems = allItems.filter {
            it.item.status == "SOLD" && (it.item.soldAt ?: it.item.createdAt) < thresholdTime
        }
        var totalDeleted = 0
        for (itemWithPhotos in soldItems) {
            totalDeleted += repository.getPhotoStorageManager().deletePhotoFiles(itemWithPhotos.photos)
        }
        totalDeleted
    }
}
