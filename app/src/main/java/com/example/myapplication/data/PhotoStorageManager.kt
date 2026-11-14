package com.example.myapplication.data

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Shader
import android.media.ExifInterface
import android.media.MediaScannerConnection
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import androidx.core.content.FileProvider
import com.example.myapplication.data.local.PhotoEntity
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.zip.Deflater
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

data class ThumbnailStorageStats(
    val thumbnailCount: Int = 0,
    val thumbnailTotalBytes: Long = 0L,
    val originalCount: Int = 0,
    val originalTotalBytes: Long = 0L
)

data class ThumbnailCompressionEstimate(
    val estimatedBytes: Long = 0L,
    val baselineBytes: Long = 0L,
    val reductionPercent: Int = 0,
    val usedExistingPhoto: Boolean = false
)

class PhotoStorageManager(private val context: Context) {

    fun getItemImageDir(itemTitle: String): File {
        val safeTitle = itemTitle.replace(Regex("[^a-zA-Z0-9_\\-\\u3040-\\u309F\\u30A0-\\u30FF\\u4E00-\\u9FFF]"), "_")
            .ifBlank { "Item" }
        val extFilesDir = context.getExternalFilesDir(Environment.DIRECTORY_PICTURES)
        val baseDir = extFilesDir ?: File(context.filesDir, "Pictures")
        val appDir = File(baseDir, "FleaMarket/$safeTitle")
        if (!appDir.exists()) {
            appDir.mkdirs()
        }
        return appDir
    }

    private fun getThumbnailDir(): File {
        val dir = File(context.filesDir, "Thumbnails")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    /**
     * ファイルのMD5チェックサムハッシュ値を計算して返却
     */
    fun calculateMd5(file: File): String {
        if (!file.exists() || !file.isFile) return ""
        return try {
            val digest = MessageDigest.getInstance("MD5")
            file.inputStream().use { input ->
                val buffer = ByteArray(8192)
                var bytesRead: Int
                while (input.read(buffer).also { bytesRead = it } != -1) {
                    digest.update(buffer, 0, bytesRead)
                }
            }
            digest.digest().joinToString("") { "%02x".format(it) }
        } catch (e: Exception) {
            e.printStackTrace()
            ""
        }
    }

    fun calculateMd5(bytes: ByteArray): String {
        return try {
            val digest = MessageDigest.getInstance("MD5")
            digest.digest(bytes).joinToString("") { "%02x".format(it) }
        } catch (e: Exception) {
            ""
        }
    }

    fun measureLocalStorageStats(photos: List<PhotoEntity>): ThumbnailStorageStats {
        val thumbDir = getThumbnailDir()
        val thumbFilesMap = mutableMapOf<String, File>()
        thumbDir.listFiles()?.filter { it.isFile }?.forEach { file ->
            thumbFilesMap[file.absolutePath] = file
        }

        val originalFilesMap = mutableMapOf<String, File>()
        for (photo in photos) {
            if (!photo.thumbnailPath.isNullOrBlank()) {
                val tf = File(photo.thumbnailPath)
                if (tf.exists() && tf.isFile) {
                    thumbFilesMap[tf.absolutePath] = tf
                }
            }
            if (photo.filePath.isNotBlank() && !photo.filePath.equals(photo.thumbnailPath, ignoreCase = true)) {
                val of = File(photo.filePath)
                if (of.exists() && of.isFile && of.parentFile?.absolutePath != thumbDir.absolutePath) {
                    originalFilesMap[of.absolutePath] = of
                }
            }
        }

        return ThumbnailStorageStats(
            thumbnailCount = thumbFilesMap.size,
            thumbnailTotalBytes = thumbFilesMap.values.sumOf { it.length() },
            originalCount = originalFilesMap.size,
            originalTotalBytes = originalFilesMap.values.sumOf { it.length() }
        )
    }

    fun estimateCompressedThumbnail(
        maxDim: Int,
        quality: Int,
        candidatePaths: List<String> = emptyList()
    ): ThumbnailCompressionEstimate {
        var sourceBitmap: Bitmap? = null
        var usedExisting = false
        try {
            for (path in candidatePaths) {
                val file = File(path)
                if (!file.exists() || !file.isFile) continue
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeFile(file.absolutePath, bounds)
                if (maxOf(bounds.outWidth, bounds.outHeight) >= 640) {
                    var sample = 1
                    val maxOrig = maxOf(bounds.outWidth, bounds.outHeight)
                    while (maxOrig / (sample * 2) >= 1200) {
                        sample *= 2
                    }
                    val decOpts = BitmapFactory.Options().apply { inSampleSize = sample }
                    val decoded = BitmapFactory.decodeFile(file.absolutePath, decOpts)
                    if (decoded != null) {
                        sourceBitmap = decoded
                        usedExisting = true
                        break
                    }
                }
            }

            if (sourceBitmap == null) {
                sourceBitmap = createRealisticSampleBitmap(1080, 1080)
            }

            val src = sourceBitmap!!
            val w = src.width
            val h = src.height

            val baseOut = ByteArrayOutputStream()
            src.compress(Bitmap.CompressFormat.JPEG, 92, baseOut)
            val baselineBytes = baseOut.size().toLong().coerceAtLeast(1L)

            val scale = minOf(maxDim.toFloat() / w, maxDim.toFloat() / h, 1.0f)
            val targetW = (w * scale).toInt().coerceAtLeast(1)
            val targetH = (h * scale).toInt().coerceAtLeast(1)
            val scaled = Bitmap.createScaledBitmap(src, targetW, targetH, true)

            val thumbOut = ByteArrayOutputStream()
            scaled.compress(Bitmap.CompressFormat.JPEG, quality.coerceIn(30, 100), thumbOut)
            val estimatedBytes = thumbOut.size().toLong().coerceAtLeast(1L)

            if (scaled != src) scaled.recycle()
            src.recycle()

            val reduction = ((1.0 - estimatedBytes.toDouble() / baselineBytes.toDouble()) * 100.0)
                .toInt()
                .coerceIn(0, 99)

            return ThumbnailCompressionEstimate(
                estimatedBytes = estimatedBytes,
                baselineBytes = baselineBytes,
                reductionPercent = reduction,
                usedExistingPhoto = usedExisting
            )
        } catch (e: Exception) {
            sourceBitmap?.recycle()
            return ThumbnailCompressionEstimate()
        }
    }

    private fun createRealisticSampleBitmap(width: Int, height: Int): Bitmap {
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val bgPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            shader = LinearGradient(
                0f, 0f, width.toFloat(), height.toFloat(),
                intArrayOf(Color.rgb(65, 92, 168), Color.rgb(220, 148, 96), Color.rgb(72, 164, 136)),
                null,
                Shader.TileMode.CLAMP
            )
        }
        canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), bgPaint)

        val detailPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { strokeWidth = 2f }
        for (i in 0 until 70) {
            detailPaint.color = Color.argb(
                90,
                (i * 37) % 256,
                (i * 73 + 60) % 256,
                (i * 113 + 120) % 256
            )
            val cx = ((i * 157) % width).toFloat()
            val cy = ((i * 263) % height).toFloat()
            canvas.drawCircle(cx, cy, 24f + (i % 5) * 18f, detailPaint)
            canvas.drawLine(0f, (i * 16).toFloat(), width.toFloat(), (height - i * 16).toFloat(), detailPaint)
        }
        return bitmap
    }

    /**
     * 画像URIからオリジナル画像を保存し、指定画質・サイズで軽量サムネイルも自動生成
     */
    fun savePhotoFromUri(
        itemTitle: String,
        sourceUri: Uri,
        thumbQuality: Int = 75,
        thumbMaxDim: Int = 480
    ): Pair<String, String?>? {
        return try {
            val dir = getItemImageDir(itemTitle)
            val timeStamp = SimpleDateFormat("yyyyMMdd_HHmmss_SSS", Locale.getDefault()).format(Date())
            val originalFile = File(dir, "IMG_$timeStamp.jpg")

            val inputStream = context.contentResolver.openInputStream(sourceUri) ?: return null
            inputStream.use { input ->
                FileOutputStream(originalFile).use { output ->
                    input.copyTo(output)
                }
            }
            if (!originalFile.exists() || originalFile.length() <= 0L) {
                try {
                    if (originalFile.exists()) originalFile.delete()
                } catch (_: Exception) {
                }
                return null
            }

            // 端末ギャラリーにも自動同期（外部アプリから参照可能）
            syncFileToPublicGallery(itemTitle, originalFile)

            // 軽量サムネイル生成
            val thumbPath = createThumbnail(originalFile, thumbMaxDim, thumbQuality)

            Pair(originalFile.absolutePath, thumbPath)
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }

    /**
     * カメラ撮影用のテンポラリURIを作成
     */
    fun createTempCameraPhotoUri(itemTitle: String): Pair<Uri, String>? {
        return try {
            val dir = getItemImageDir(itemTitle)
            val timeStamp = SimpleDateFormat("yyyyMMdd_HHmmss_SSS", Locale.getDefault()).format(Date())
            val file = File(dir, "CAMERA_$timeStamp.jpg")
            val authority = "${context.packageName}.fileprovider"
            val uri = FileProvider.getUriForFile(context, authority, file)
            Pair(uri, file.absolutePath)
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }

    /**
     * 既存の写真ファイルからサムネイルを生成
     */
    fun createThumbnail(sourceFile: File, maxDim: Int = 480, quality: Int = 75): String? {
        if (!sourceFile.exists()) return null
        return try {
            val options = BitmapFactory.Options().apply {
                inJustDecodeBounds = true
            }
            BitmapFactory.decodeFile(sourceFile.absolutePath, options)

            var inSampleSize = 1
            val maxOriginal = maxOf(options.outWidth, options.outHeight)
            if (maxOriginal > maxDim) {
                inSampleSize = maxOriginal / maxDim
            }

            val decodeOptions = BitmapFactory.Options().apply {
                this.inSampleSize = inSampleSize.coerceAtLeast(1)
            }
            val decodedBitmap = BitmapFactory.decodeFile(sourceFile.absolutePath, decodeOptions) ?: return null
            val orientation = try {
                ExifInterface(sourceFile.absolutePath).getAttributeInt(
                    ExifInterface.TAG_ORIENTATION,
                    ExifInterface.ORIENTATION_NORMAL
                )
            } catch (_: Exception) {
                ExifInterface.ORIENTATION_NORMAL
            }
            val bitmap = applyExifOrientation(decodedBitmap, orientation)

            // アスペクト比を保ってリサイズ
            val w = bitmap.width
            val h = bitmap.height
            val scale = minOf(maxDim.toFloat() / w, maxDim.toFloat() / h, 1.0f)
            val targetW = (w * scale).toInt().coerceAtLeast(1)
            val targetH = (h * scale).toInt().coerceAtLeast(1)

            val scaledBitmap = Bitmap.createScaledBitmap(bitmap, targetW, targetH, true)

            val thumbFile = File(getThumbnailDir(), "THUMB_${sourceFile.name}")
            FileOutputStream(thumbFile).use { out ->
                scaledBitmap.compress(Bitmap.CompressFormat.JPEG, quality.coerceIn(30, 100), out)
            }

            if (scaledBitmap != bitmap) bitmap.recycle()
            scaledBitmap.recycle()

            thumbFile.absolutePath
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }

    /**
     * 端末の「Pictures/FleaMarket/{商品名}」共有領域へ写真を同期
     */
    fun syncFileToPublicGallery(itemTitle: String, localFile: File): Uri? {
        if (!localFile.exists()) return null
        val safeTitle = itemTitle.replace(Regex("[^a-zA-Z0-9_\\-\\u3040-\\u309F\\u30A0-\\u30FF\\u4E00-\\u9FFF]"), "_")
            .ifBlank { "Item" }

        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                val values = ContentValues().apply {
                    put(MediaStore.Images.Media.DISPLAY_NAME, localFile.name)
                    put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg")
                    put(MediaStore.Images.Media.RELATIVE_PATH, "${Environment.DIRECTORY_PICTURES}/FleaMarket/$safeTitle")
                    put(MediaStore.Images.Media.IS_PENDING, 1)
                }
                val uri = context.contentResolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values)
                if (uri != null) {
                    FileInputStream(localFile).use { input ->
                        context.contentResolver.openOutputStream(uri)?.use { output ->
                            input.copyTo(output)
                        }
                    }
                    values.clear()
                    values.put(MediaStore.Images.Media.IS_PENDING, 0)
                    context.contentResolver.update(uri, values, null, null)
                    uri
                } else null
            } else {
                val publicDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES)
                val targetDir = File(publicDir, "FleaMarket/$safeTitle").apply { mkdirs() }
                val targetFile = File(targetDir, localFile.name)
                FileInputStream(localFile).use { input ->
                    FileOutputStream(targetFile).use { output ->
                        input.copyTo(output)
                    }
                }
                MediaScannerConnection.scanFile(
                    context,
                    arrayOf(targetFile.absolutePath),
                    arrayOf("image/jpeg"),
                    null
                )
                Uri.fromFile(targetFile)
            }
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }

    fun createThumbnailFromBytes(
        bytes: ByteArray,
        fileNameHint: String,
        maxDim: Int = 480,
        quality: Int = 75
    ): String? {
        if (bytes.isEmpty()) return null
        return try {
            val options = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options)
            if (options.outWidth <= 0 || options.outHeight <= 0) return null

            var inSampleSize = 1
            val maxOriginal = maxOf(options.outWidth, options.outHeight)
            if (maxOriginal > maxDim) {
                inSampleSize = maxOriginal / maxDim
            }
            val decodeOptions = BitmapFactory.Options().apply {
                this.inSampleSize = inSampleSize.coerceAtLeast(1)
            }
            val decodedBitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, decodeOptions) ?: return null
            val orientation = try {
                ExifInterface(ByteArrayInputStream(bytes)).getAttributeInt(
                    ExifInterface.TAG_ORIENTATION,
                    ExifInterface.ORIENTATION_NORMAL
                )
            } catch (_: Exception) {
                ExifInterface.ORIENTATION_NORMAL
            }
            val bitmap = applyExifOrientation(decodedBitmap, orientation)

            val w = bitmap.width
            val h = bitmap.height
            val scale = minOf(maxDim.toFloat() / w, maxDim.toFloat() / h, 1.0f)
            val targetW = (w * scale).toInt().coerceAtLeast(1)
            val targetH = (h * scale).toInt().coerceAtLeast(1)
            val scaledBitmap = Bitmap.createScaledBitmap(bitmap, targetW, targetH, true)

            val safeHint = fileNameHint.replace(Regex("[^a-zA-Z0-9_.\\-]"), "_").ifBlank { "repaired.jpg" }
            val thumbFile = File(getThumbnailDir(), "THUMB_$safeHint")
            FileOutputStream(thumbFile).use { out ->
                scaledBitmap.compress(Bitmap.CompressFormat.JPEG, quality.coerceIn(30, 100), out)
            }

            if (scaledBitmap != bitmap) bitmap.recycle()
            scaledBitmap.recycle()

            thumbFile.absolutePath
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }

    private fun applyExifOrientation(bitmap: Bitmap, orientation: Int): Bitmap {
        val matrix = Matrix()
        when (orientation) {
            ExifInterface.ORIENTATION_ROTATE_90 -> matrix.postRotate(90f)
            ExifInterface.ORIENTATION_ROTATE_180 -> matrix.postRotate(180f)
            ExifInterface.ORIENTATION_ROTATE_270 -> matrix.postRotate(270f)
            ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrix.postScale(-1f, 1f)
            ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrix.postScale(1f, -1f)
            ExifInterface.ORIENTATION_TRANSPOSE -> {
                matrix.postRotate(90f)
                matrix.postScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_TRANSVERSE -> {
                matrix.postRotate(270f)
                matrix.postScale(-1f, 1f)
            }
            else -> return bitmap
        }
        return try {
            val rotated = Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, matrix, true)
            if (rotated != bitmap) bitmap.recycle()
            rotated
        } catch (_: Exception) {
            bitmap
        }
    }

    fun saveBytesToPublicGallery(itemTitle: String, fileName: String, bytes: ByteArray): Uri? {
        if (bytes.isEmpty()) return null
        val safeTitle = itemTitle.replace(Regex("[^a-zA-Z0-9_\\-\\u3040-\\u309F\\u30A0-\\u30FF\\u4E00-\\u9FFF]"), "_")
            .ifBlank { "Item" }
        val safeFileName = fileName.replace(Regex("[^a-zA-Z0-9_.\\-]"), "_").ifBlank { "photo.jpg" }

        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                val values = ContentValues().apply {
                    put(MediaStore.Images.Media.DISPLAY_NAME, safeFileName)
                    put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg")
                    put(MediaStore.Images.Media.RELATIVE_PATH, "${Environment.DIRECTORY_PICTURES}/FleaMarket/$safeTitle")
                    put(MediaStore.Images.Media.IS_PENDING, 1)
                }
                val uri = context.contentResolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values)
                if (uri != null) {
                    context.contentResolver.openOutputStream(uri)?.use { output ->
                        output.write(bytes)
                    }
                    values.clear()
                    values.put(MediaStore.Images.Media.IS_PENDING, 0)
                    context.contentResolver.update(uri, values, null, null)
                    uri
                } else null
            } else {
                val publicDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES)
                val targetDir = File(publicDir, "FleaMarket/$safeTitle").apply { mkdirs() }
                val targetFile = File(targetDir, safeFileName)
                FileOutputStream(targetFile).use { output ->
                    output.write(bytes)
                }
                MediaScannerConnection.scanFile(
                    context,
                    arrayOf(targetFile.absolutePath),
                    arrayOf("image/jpeg"),
                    null
                )
                Uri.fromFile(targetFile)
            }
        } catch (e: Exception) {
            e.printStackTrace()
            null
        }
    }

    fun exportPhotosToGallery(itemTitle: String, filePaths: List<String>): Int {
        var count = 0
        for (path in filePaths) {
            val file = File(path)
            if (file.exists()) {
                val result = syncFileToPublicGallery(itemTitle, file)
                if (result != null) count++
            }
        }
        return count
    }

    /**
     * PC送信用：商品タイトル名の無圧縮ZIPファイルを作成し、商品説明文テキストも同梱する
     */
    fun createStoredZipForSharing(
        itemTitle: String,
        description: String?,
        photoPaths: List<String>
    ): Intent? {
        val safeTitle = itemTitle.replace(Regex("[^a-zA-Z0-9_\\-\\u3040-\\u309F\\u30A0-\\u30FF\\u4E00-\\u9FFF]"), "_")
            .ifBlank { "Item" }

        val cacheExportsDir = File(context.cacheDir, "shared_zips")
        if (!cacheExportsDir.exists()) cacheExportsDir.mkdirs()

        val zipFile = File(cacheExportsDir, "$safeTitle.zip")

        try {
            FileOutputStream(zipFile).use { fos ->
                ZipOutputStream(fos).use { zos ->
                    // 無圧縮（STORE方式）で高速化・PC側でフォルダ同様に即展開なしで開ける
                    zos.setLevel(Deflater.NO_COMPRESSION)

                    // 1. 商品説明文テキスト（入力がある場合）
                    if (!description.isNullOrBlank()) {
                        val descEntry = ZipEntry("商品説明.txt")
                        zos.putNextEntry(descEntry)
                        val descContent = buildString {
                            appendLine("【商品名】 $itemTitle")
                            appendLine("----------------------------------------")
                            appendLine(description)
                        }
                        zos.write(descContent.toByteArray(Charsets.UTF_8))
                        zos.closeEntry()
                    }

                    // 2. 写真ファイル群
                    photoPaths.forEachIndexed { index, path ->
                        val srcFile = File(path)
                        if (srcFile.exists()) {
                            val photoEntry = ZipEntry(String.format(Locale.getDefault(), "%02d_%s", index + 1, srcFile.name))
                            zos.putNextEntry(photoEntry)
                            FileInputStream(srcFile).use { fis ->
                                fis.copyTo(zos)
                            }
                            zos.closeEntry()
                        }
                    }
                }
            }

            val authority = "${context.packageName}.fileprovider"
            val uri = FileProvider.getUriForFile(context, authority, zipFile)

            return Intent(Intent.ACTION_SEND).apply {
                type = "application/zip"
                putExtra(Intent.EXTRA_STREAM, uri)
                putExtra(Intent.EXTRA_SUBJECT, "$itemTitle (写真・説明文一式)")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
        } catch (e: Exception) {
            e.printStackTrace()
            return null
        }
    }

    /**
     * PC送信済みの元高解像度写真を削除し、軽量サムネイルのみ残してストレージを削減
     */
    fun deleteOriginalPhotosKeepThumbnails(photos: List<PhotoEntity>): Int {
        var count = 0
        for (photo in photos) {
            if (photo.thumbnailPath != null && File(photo.thumbnailPath).exists()) {
                val origFile = File(photo.filePath)
                if (origFile.exists() && origFile.absolutePath != photo.thumbnailPath) {
                    if (origFile.delete()) count++
                }
            }
        }
        return count
    }

    /**
     * 写真ファイル完全削除
     */
    fun deletePhotoFiles(photos: List<PhotoEntity>): Int {
        var count = 0
        for (photo in photos) {
            val orig = File(photo.filePath)
            if (orig.exists()) {
                if (orig.delete()) count++
            }
            if (photo.thumbnailPath != null) {
                val thumb = File(photo.thumbnailPath)
                if (thumb.exists()) thumb.delete()
            }
        }
        return count
    }
}
