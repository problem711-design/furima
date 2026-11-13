package com.example

import android.app.Application
import android.graphics.Bitmap
import androidx.test.core.app.ApplicationProvider
import com.example.myapplication.data.PhotoStorageManager
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import java.io.ByteArrayOutputStream
import java.io.File

@RunWith(RobolectricTestRunner::class)
class PhotoStorageManagerTest {

    @Test
    fun calculateMd5Bytes_returnsExpectedDigest() {
        val context = ApplicationProvider.getApplicationContext<Application>()
        val manager = PhotoStorageManager(context)
        val digest = manager.calculateMd5("furima-test".toByteArray(Charsets.UTF_8))
        assertEquals(32, digest.length)
        assertEquals(digest, manager.calculateMd5("furima-test".toByteArray(Charsets.UTF_8)))
    }

    @Test
    fun estimateCompressedThumbnail_variesWithQualityAndDimension() {
        val context = ApplicationProvider.getApplicationContext<Application>()
        val manager = PhotoStorageManager(context)

        val lowEst = manager.estimateCompressedThumbnail(maxDim = 320, quality = 50)
        val highEst = manager.estimateCompressedThumbnail(maxDim = 640, quality = 90)

        assertTrue(lowEst.estimatedBytes > 0L)
        assertTrue(highEst.estimatedBytes > 0L)
        assertTrue(lowEst.estimatedBytes < highEst.estimatedBytes)
        assertTrue(lowEst.baselineBytes >= lowEst.estimatedBytes)
    }

    @Test
    fun createThumbnailFromBytes_createsReadableJpegThumbnail() {
        val context = ApplicationProvider.getApplicationContext<Application>()
        val manager = PhotoStorageManager(context)

        val bmp = Bitmap.createBitmap(800, 600, Bitmap.Config.ARGB_8888)
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, 90, out)
        bmp.recycle()

        val thumbPath = manager.createThumbnailFromBytes(
            bytes = out.toByteArray(),
            fileNameHint = "repair_test.jpg",
            maxDim = 480,
            quality = 75
        )
        assertNotNull(thumbPath)
        val thumbFile = File(thumbPath!!)
        assertTrue(thumbFile.exists() && thumbFile.length() > 0L)
    }
}
