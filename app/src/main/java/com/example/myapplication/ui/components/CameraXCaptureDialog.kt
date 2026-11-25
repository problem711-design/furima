package com.example.myapplication.ui.components

import android.content.Context
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraMetadata
import android.hardware.camera2.CaptureRequest
import android.util.Range
import android.view.Surface
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.annotation.OptIn
import androidx.camera.camera2.interop.Camera2CameraControl
import androidx.camera.camera2.interop.Camera2CameraInfo
import androidx.camera.camera2.interop.CaptureRequestOptions
import androidx.camera.camera2.interop.ExperimentalCamera2Interop
import androidx.camera.core.Camera
import androidx.camera.core.CameraInfo
import androidx.camera.core.CameraSelector
import androidx.camera.core.FocusMeteringAction
import androidx.camera.core.ImageCapture
import androidx.camera.core.ImageCaptureException
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Cameraswitch
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.FlashAuto
import androidx.compose.material.icons.filled.FlashOff
import androidx.compose.material.icons.filled.FlashOn
import androidx.compose.material.icons.filled.FlashlightOff
import androidx.compose.material.icons.filled.FlashlightOn
import androidx.compose.material.icons.filled.GridOff
import androidx.compose.material.icons.filled.GridOn
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material.icons.filled.Undo
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.zIndex
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import coil.compose.AsyncImage
import coil.request.ImageRequest
import coil.size.Precision
import com.example.myapplication.data.PhotoStorageManager
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.util.Locale
import java.util.concurrent.TimeUnit
import kotlin.math.roundToInt

@Composable
fun CameraXCaptureDialog(
    itemTitle: String,
    onPhotosCaptured: (List<String>) -> Unit,
    onDismiss: () -> Unit
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val coroutineScope = rememberCoroutineScope()
    val storageManager = remember(context) { PhotoStorageManager(context) }
    val savedSettings = remember(context) { CameraXCapturePreferences.load(context) }

    var capturedPaths by rememberSaveable { mutableStateOf(emptyList<String>()) }
    var pendingDiscardPaths by rememberSaveable { mutableStateOf(emptyList<String>()) }
    var pendingTempFiles by rememberSaveable { mutableStateOf(emptyList<String>()) }
    var previewCapturedIndex by rememberSaveable { mutableStateOf<Int?>(null) }
    var lastDeletedShot by remember { mutableStateOf<Pair<Int, String>?>(null) }
    var flashMode by rememberSaveable { mutableIntStateOf(savedSettings.flashMode) }
    var isTorchOn by rememberSaveable { mutableStateOf(false) }
    var isHighResMode by rememberSaveable { mutableStateOf(savedSettings.isHighResMode) }
    var guideModeOrdinal by rememberSaveable { mutableIntStateOf(savedSettings.guideModeOrdinal) }
    val guideMode = CameraGuideMode.entries.getOrElse(guideModeOrdinal) { CameraGuideMode.GRID_3X3 }

    var selectedCameraId by rememberSaveable { mutableStateOf(savedSettings.selectedCameraId) }
    var lensOptions by remember { mutableStateOf<List<CameraLensOption>>(emptyList()) }

    var camera by remember { mutableStateOf<Camera?>(null) }
    var imageCapture by remember { mutableStateOf<ImageCapture?>(null) }
    var cameraProvider by remember { mutableStateOf<ProcessCameraProvider?>(null) }
    var previewViewRef by remember { mutableStateOf<PreviewView?>(null) }

    var minZoom by remember { mutableFloatStateOf(1f) }
    var maxZoom by remember { mutableFloatStateOf(4f) }
    var currentZoom by remember { mutableFloatStateOf(savedSettings.zoomRatio) }

    var evSupported by remember { mutableStateOf(false) }
    var evLower by remember { mutableIntStateOf(0) }
    var evUpper by remember { mutableIntStateOf(0) }
    var evStep by remember { mutableFloatStateOf(0.33f) }
    var evIndex by remember { mutableIntStateOf(savedSettings.evIndex) }
    var hasFlashUnit by remember { mutableStateOf(false) }

    var cam2Caps by remember { mutableStateOf(Camera2Capabilities()) }
    var showProPanel by remember { mutableStateOf(savedSettings.showProPanel) }
    var manualIso by remember { mutableStateOf(savedSettings.manualIso) }
    var manualShutterNanos by remember { mutableStateOf(savedSettings.manualShutterNanos) }
    var manualAwbMode by remember { mutableStateOf(savedSettings.manualAwbMode) }
    var hasAppliedInitialRestore by remember { mutableStateOf(false) }
    var isCameraSettingsReady by remember { mutableStateOf(false) }

    var focusPoint by remember { mutableStateOf<Offset?>(null) }
    var isCapturing by remember { mutableStateOf(false) }
    var showShutterFlash by remember { mutableStateOf(false) }
    var showCloseConfirm by remember { mutableStateOf(false) }
    var initError by remember { mutableStateOf<String?>(null) }

    LaunchedEffect(focusPoint) {
        if (focusPoint != null) {
            delay(1200)
            focusPoint = null
        }
    }

    LaunchedEffect(showShutterFlash) {
        if (showShutterFlash) {
            delay(120)
            showShutterFlash = false
        }
    }

    val purgePendingDiscards = {
        val toDelete = (pendingDiscardPaths + pendingTempFiles).distinct()
        pendingDiscardPaths = emptyList()
        pendingTempFiles = emptyList()
        lastDeletedShot = null
        if (toDelete.isNotEmpty()) {
            CoroutineScope(Dispatchers.IO + NonCancellable).launch {
                toDelete.forEach { path ->
                    try {
                        File(path).delete()
                    } catch (_: Exception) {
                    }
                }
            }
        }
    }

    val handleCloseRequest: () -> Unit = {
        if (isCapturing) {
            Toast.makeText(context, "写真の保存処理中です。完了後にもう一度閉じてください", Toast.LENGTH_SHORT).show()
        } else if (capturedPaths.isNotEmpty()) {
            showCloseConfirm = true
        } else {
            purgePendingDiscards()
            onDismiss()
        }
    }

    BackHandler(onBack = handleCloseRequest)

    if (showCloseConfirm) {
        AlertDialog(
            onDismissRequest = { showCloseConfirm = false },
            title = { Text("撮影済み写真 (${capturedPaths.size}枚) の扱い") },
            text = { Text("撮影した写真を商品へ追加して閉じますか？それとも破棄しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        showCloseConfirm = false
                        purgePendingDiscards()
                        onPhotosCaptured(capturedPaths)
                    },
                    modifier = Modifier.testTag("camerax_confirm_save_close")
                ) {
                    Text("追加して閉じる (${capturedPaths.size}枚)")
                }
            },
            dismissButton = {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(onClick = { showCloseConfirm = false }) {
                        Text("撮影を続ける")
                    }
                    TextButton(
                        onClick = {
                            capturedPaths.forEach { path ->
                                try {
                                    File(path).delete()
                                } catch (_: Exception) {
                                }
                            }
                            capturedPaths = emptyList()
                            purgePendingDiscards()
                            showCloseConfirm = false
                            onDismiss()
                        },
                        modifier = Modifier.testTag("camerax_discard_close")
                    ) {
                        Text("破棄して閉じる", color = MaterialTheme.colorScheme.error)
                    }
                }
            }
        )
    }

    // ProcessCameraProvider 初期化
    DisposableEffect(context) {
        val future = ProcessCameraProvider.getInstance(context)
        val mainExecutor = ContextCompat.getMainExecutor(context)
        var providerInstance: ProcessCameraProvider? = null
        future.addListener({
            try {
                val provider = future.get()
                providerInstance = provider
                val discovered = discoverCameraLenses(provider)
                lensOptions = discovered
                if (selectedCameraId == null || discovered.none { it.cameraId == selectedCameraId }) {
                    selectedCameraId = discovered.firstOrNull {
                        it.lensFacing == CameraCharacteristics.LENS_FACING_BACK
                    }?.cameraId ?: discovered.firstOrNull()?.cameraId
                }
                cameraProvider = provider
            } catch (e: Exception) {
                initError = "カメラの初期化に失敗しました: ${e.message ?: "不明なエラー"}"
            }
        }, mainExecutor)

        onDispose {
            try {
                providerInstance?.unbindAll()
            } catch (_: Exception) {
            }
        }
    }

    // カメラバインド (レンズ切替・PreviewView準備完了時に再バインド)
    LaunchedEffect(cameraProvider, previewViewRef, selectedCameraId, lifecycleOwner) {
        val provider = cameraProvider ?: return@LaunchedEffect
        val previewView = previewViewRef ?: return@LaunchedEffect

        isCameraSettingsReady = false
        try {
            provider.unbindAll()
            val preview = Preview.Builder().build().apply {
                surfaceProvider = previewView.surfaceProvider
            }
            val capture = ImageCapture.Builder()
                .setCaptureMode(ImageCapture.CAPTURE_MODE_MINIMIZE_LATENCY)
                .setFlashMode(flashMode)
                .build()

            val selector = buildCameraSelector(selectedCameraId)
            val boundCamera = provider.bindToLifecycle(
                lifecycleOwner,
                selector,
                preview,
                capture
            )
            camera = boundCamera
            imageCapture = capture
            isTorchOn = false

            // Zoom / Exposure / Flash / Camera2 機能プローブ
            val info = boundCamera.cameraInfo
            val zoomState = info.zoomState.value
            minZoom = zoomState?.minZoomRatio ?: 1f
            maxZoom = (zoomState?.maxZoomRatio ?: 4f).coerceAtLeast(minZoom)

            val expState = info.exposureState
            evSupported = expState.isExposureCompensationSupported
            evLower = expState.exposureCompensationRange.lower
            evUpper = expState.exposureCompensationRange.upper
            evStep = expState.exposureCompensationStep.toFloat().let { if (it > 0f) it else 0.33f }
            hasFlashUnit = info.hasFlashUnit()

            val caps = readCamera2Capabilities(info)
            cam2Caps = caps

            if (!hasAppliedInitialRestore) {
                hasAppliedInitialRestore = true
                val targetZoom = currentZoom.coerceIn(minZoom, maxZoom)
                currentZoom = targetZoom
                if (kotlin.math.abs(targetZoom - (zoomState?.zoomRatio ?: 1f)) > 0.01f) {
                    try {
                        boundCamera.cameraControl.setZoomRatio(targetZoom)
                    } catch (_: Exception) {
                    }
                }

                val safeIso = manualIso?.let { iso ->
                    val r = caps.isoRange
                    if (caps.supportsManualSensor && r != null) {
                        val minI = r.lower.coerceAtLeast(50)
                        val maxI = r.upper.coerceAtMost(3200).coerceAtLeast(minI)
                        iso.coerceIn(minI, maxI)
                    } else null
                }
                val safeShutter = manualShutterNanos?.let { ns ->
                    val r = caps.exposureTimeRange
                    if (caps.supportsManualSensor && r != null && ns in r.lower..r.upper) ns else null
                }
                val safeAwb = manualAwbMode?.takeIf { caps.availableAwbModes.contains(it) }
                val hasPro = caps.supportsManualSensor || caps.availableAwbModes.size > 1
                manualIso = safeIso
                manualShutterNanos = safeShutter
                manualAwbMode = safeAwb
                showProPanel = showProPanel && hasPro

                if (evSupported && evUpper > evLower && safeIso == null && safeShutter == null) {
                    val targetEv = evIndex.coerceIn(evLower, evUpper)
                    evIndex = targetEv
                    if (targetEv != expState.exposureCompensationIndex) {
                        try {
                            boundCamera.cameraControl.setExposureCompensationIndex(targetEv)
                        } catch (_: Exception) {
                        }
                    }
                } else {
                    evIndex = 0
                }
            } else {
                currentZoom = zoomState?.zoomRatio ?: 1f
                evIndex = expState.exposureCompensationIndex
                manualIso = null
                manualShutterNanos = null
                manualAwbMode = null
                showProPanel = false
            }

            initError = null
            delay(150)
            isCameraSettingsReady = true
        } catch (e: Exception) {
            initError = "カメラを起動できませんでした: ${e.message ?: "不明なエラー"}"
        }
    }

    // ZoomState 監視
    val activeCamera = camera
    DisposableEffect(activeCamera, lifecycleOwner) {
        val observer = androidx.lifecycle.Observer<androidx.camera.core.ZoomState> { zs ->
            minZoom = zs.minZoomRatio
            maxZoom = zs.maxZoomRatio.coerceAtLeast(zs.minZoomRatio)
            if (isCameraSettingsReady) {
                currentZoom = zs.zoomRatio
            }
        }
        activeCamera?.cameraInfo?.zoomState?.observe(lifecycleOwner, observer)
        onDispose {
            activeCamera?.cameraInfo?.zoomState?.removeObserver(observer)
        }
    }

    // フラッシュモード反映
    LaunchedEffect(flashMode, imageCapture) {
        imageCapture?.flashMode = flashMode
    }

    // Camera2Interop 手動設定適用
    LaunchedEffect(activeCamera, manualIso, manualShutterNanos, manualAwbMode, cam2Caps) {
        val cam = activeCamera ?: return@LaunchedEffect
        applyCamera2ManualControls(
            camera = cam,
            caps = cam2Caps,
            manualIso = manualIso,
            manualShutterNanos = manualShutterNanos,
            manualAwbMode = manualAwbMode
        )
    }

    // 完全記憶型：スタジオ撮影用のカメラ設定を自動永続化（ライト常時点灯は安全のため除外）
    LaunchedEffect(
        isCameraSettingsReady,
        selectedCameraId,
        flashMode,
        guideModeOrdinal,
        currentZoom,
        evIndex,
        showProPanel,
        manualIso,
        manualShutterNanos,
        manualAwbMode,
        isHighResMode
    ) {
        if (!isCameraSettingsReady) return@LaunchedEffect
        delay(300)
        withContext(Dispatchers.IO) {
            CameraXCapturePreferences.save(
            context = context,
            settings = SavedCameraSettings(
                selectedCameraId = selectedCameraId,
                flashMode = flashMode,
                guideModeOrdinal = guideModeOrdinal,
                zoomRatio = currentZoom,
                evIndex = evIndex,
                showProPanel = showProPanel,
                manualIso = manualIso,
                manualShutterNanos = manualShutterNanos,
                manualAwbMode = manualAwbMode,
                isHighResMode = isHighResMode
            )
        )
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .zIndex(100f)
            .background(Color.Black)
            .pointerInput(Unit) {
                awaitPointerEventScope {
                    while (true) {
                        awaitPointerEvent()
                    }
                }
            }
            .testTag("camerax_capture_dialog")
    ) {
            // 1. カメラプレビュー (TextureView 互換モードで Xiaomi HyperOS の Dialog 重畳を安定化)
            AndroidView(
                factory = { ctx ->
                    PreviewView(ctx).apply {
                        scaleType = PreviewView.ScaleType.FILL_CENTER
                        implementationMode = PreviewView.ImplementationMode.COMPATIBLE
                        previewViewRef = this
                    }
                },
                modifier = Modifier
                    .fillMaxSize()
                    .pointerInput(activeCamera, previewViewRef) {
                        detectTapGestures { offset ->
                            val pv = previewViewRef ?: return@detectTapGestures
                            val cam = activeCamera ?: return@detectTapGestures
                            focusPoint = offset
                            try {
                                val point = pv.meteringPointFactory.createPoint(offset.x, offset.y)
                                val action = FocusMeteringAction.Builder(
                                    point,
                                    FocusMeteringAction.FLAG_AF or FocusMeteringAction.FLAG_AE
                                )
                                    .setAutoCancelDuration(3, TimeUnit.SECONDS)
                                    .build()
                                cam.cameraControl.startFocusAndMetering(action)
                            } catch (_: Exception) {
                            }
                        }
                    }
                    .pointerInput(activeCamera, minZoom, maxZoom) {
                        detectTransformGestures { _, _, zoom, _ ->
                            val cam = activeCamera ?: return@detectTransformGestures
                            if (zoom != 1f) {
                                val target = (currentZoom * zoom).coerceIn(minZoom, maxZoom)
                                cam.cameraControl.setZoomRatio(target)
                            }
                        }
                    }
            )

            // 2. 構図ガイド (3×3 グリッド / 1対1 出品枠)
            if (guideMode != CameraGuideMode.NONE) {
                Canvas(modifier = Modifier.fillMaxSize()) {
                    val w = size.width
                    val h = size.height

                    if (guideMode == CameraGuideMode.GRID_AND_SQUARE) {
                        val side = minOf(w, h)
                        val left = (w - side) / 2f
                        val top = (h - side) / 2f
                        val shade = Color.Black.copy(alpha = 0.38f)
                        if (top > 0f) {
                            drawRect(shade, Offset.Zero, Size(w, top))
                            drawRect(shade, Offset(0f, top + side), Size(w, h - (top + side)))
                        }
                        if (left > 0f) {
                            drawRect(shade, Offset(0f, top), Size(left, side))
                            drawRect(shade, Offset(left + side, top), Size(w - (left + side), side))
                        }
                        drawRect(
                            color = Color.White.copy(alpha = 0.85f),
                            topLeft = Offset(left, top),
                            size = Size(side, side),
                            style = Stroke(width = 2.dp.toPx())
                        )
                    }

                    val gridColor = Color.White.copy(alpha = 0.38f)
                    val strokePx = 1.dp.toPx()
                    drawLine(gridColor, Offset(w / 3f, 0f), Offset(w / 3f, h), strokePx)
                    drawLine(gridColor, Offset(w * 2f / 3f, 0f), Offset(w * 2f / 3f, h), strokePx)
                    drawLine(gridColor, Offset(0f, h / 3f), Offset(w, h / 3f), strokePx)
                    drawLine(gridColor, Offset(0f, h * 2f / 3f), Offset(w, h * 2f / 3f), strokePx)
                }
            }

            // 3. タップフォーカスリング
            focusPoint?.let { pt ->
                Canvas(modifier = Modifier.fillMaxSize()) {
                    drawCircle(
                        color = Color(0xFFFACC15),
                        radius = 30.dp.toPx(),
                        center = pt,
                        style = Stroke(width = 2.dp.toPx())
                    )
                }
            }

            // 4. シャッター発光フラッシュ効果
            AnimatedVisibility(
                visible = showShutterFlash,
                enter = fadeIn(),
                exit = fadeOut()
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(Color.White.copy(alpha = 0.55f))
                )
            }

            // エラー表示
            initError?.let { msg ->
                Surface(
                    color = Color(0xFF991B1B).copy(alpha = 0.9f),
                    shape = RoundedCornerShape(12.dp),
                    modifier = Modifier
                        .align(Alignment.Center)
                        .padding(24.dp)
                ) {
                    Text(
                        text = msg,
                        color = Color.White,
                        fontSize = 13.sp,
                        modifier = Modifier.padding(16.dp)
                    )
                }
            }

            // 5. 上部コントロールバー
            val hasProSupport = cam2Caps.supportsManualSensor || cam2Caps.availableAwbModes.size > 1
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .align(Alignment.TopCenter)
                    .background(Color.Black.copy(alpha = 0.5f))
                    .statusBarsPadding()
                    .padding(horizontal = 8.dp, vertical = 6.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    IconButton(
                        onClick = handleCloseRequest,
                        modifier = Modifier
                            .size(48.dp)
                            .testTag("camerax_close_button")
                    ) {
                        Icon(
                            imageVector = Icons.Default.Close,
                            contentDescription = "カメラを閉じる",
                            tint = Color.White
                        )
                    }

                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(2.dp)
                    ) {
                        // フラッシュ切替
                        IconButton(
                            onClick = {
                                flashMode = when (flashMode) {
                                    ImageCapture.FLASH_MODE_OFF -> ImageCapture.FLASH_MODE_ON
                                    ImageCapture.FLASH_MODE_ON -> ImageCapture.FLASH_MODE_AUTO
                                    else -> ImageCapture.FLASH_MODE_OFF
                                }
                            },
                            modifier = Modifier
                                .size(48.dp)
                                .testTag("camerax_flash_button")
                        ) {
                            Icon(
                                imageVector = when (flashMode) {
                                    ImageCapture.FLASH_MODE_ON -> Icons.Default.FlashOn
                                    ImageCapture.FLASH_MODE_AUTO -> Icons.Default.FlashAuto
                                    else -> Icons.Default.FlashOff
                                },
                                contentDescription = "フラッシュ切替",
                                tint = if (flashMode == ImageCapture.FLASH_MODE_OFF) Color.White else Color(0xFFFACC15)
                            )
                        }

                        // トーチ常時点灯
                        if (hasFlashUnit) {
                            IconButton(
                                onClick = {
                                    val next = !isTorchOn
                                    isTorchOn = next
                                    activeCamera?.cameraControl?.enableTorch(next)
                                },
                                modifier = Modifier
                                    .size(48.dp)
                                    .testTag("camerax_torch_button")
                            ) {
                                Icon(
                                    imageVector = if (isTorchOn) Icons.Default.FlashlightOn else Icons.Default.FlashlightOff,
                                    contentDescription = "ライト常時点灯",
                                    tint = if (isTorchOn) Color(0xFFFACC15) else Color.White
                                )
                            }
                        }

                        // ガイド線切替 (オフ -> 3x3 -> 3x3 + 1対1枠)
                        IconButton(
                            onClick = {
                                val next = (guideMode.ordinal + 1) % CameraGuideMode.entries.size
                                guideModeOrdinal = next
                                val label = when (CameraGuideMode.entries[next]) {
                                    CameraGuideMode.NONE -> "ガイド線：オフ"
                                    CameraGuideMode.GRID_3X3 -> "3×3 グリッド表示"
                                    CameraGuideMode.GRID_AND_SQUARE -> "3×3 ＋ 1対1 出品枠表示"
                                }
                                Toast.makeText(context, label, Toast.LENGTH_SHORT).show()
                            },
                            modifier = Modifier
                                .size(48.dp)
                                .testTag("camerax_grid_button")
                        ) {
                            Icon(
                                imageVector = if (guideMode == CameraGuideMode.NONE) Icons.Default.GridOff else Icons.Default.GridOn,
                                contentDescription = "構図ガイド切替",
                                tint = when (guideMode) {
                                    CameraGuideMode.NONE -> Color.White.copy(alpha = 0.7f)
                                    CameraGuideMode.GRID_3X3 -> Color.White
                                    CameraGuideMode.GRID_AND_SQUARE -> Color(0xFF38BDF8)
                                }
                            )
                        }

                        // Camera2 手動調整パネル開閉 (対応端末のみ)
                        if (hasProSupport) {
                            IconButton(
                                onClick = { showProPanel = !showProPanel },
                                modifier = Modifier
                                    .size(48.dp)
                                    .testTag("camerax_pro_toggle_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Tune,
                                    contentDescription = "詳細設定",
                                    tint = if (showProPanel || manualIso != null || manualShutterNanos != null || manualAwbMode != null) {
                                        Color(0xFFFACC15)
                                    } else {
                                        Color.White
                                    }
                                )
                            }
                        }

                        // レンズ切替
                        if (lensOptions.size > 1) {
                            IconButton(
                                onClick = {
                                    val idx = lensOptions.indexOfFirst { it.cameraId == selectedCameraId }
                                    val nextOpt = lensOptions[(idx + 1) % lensOptions.size]
                                    selectedCameraId = nextOpt.cameraId
                                    Toast.makeText(context, "カメラ切替：${nextOpt.label}", Toast.LENGTH_SHORT).show()
                                },
                                modifier = Modifier
                                    .size(48.dp)
                                    .testTag("camerax_switch_camera_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Cameraswitch,
                                    contentDescription = "カメラ切替",
                                    tint = Color.White
                                )
                            }
                        }
                    }
                }

                // 保存解像度切替バー (標準 2048px / フル解像度)
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 4.dp, vertical = 2.dp),
                    horizontalArrangement = Arrangement.End,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "保存解像度:",
                        color = Color.White.copy(alpha = 0.78f),
                        fontSize = 10.sp,
                        modifier = Modifier.padding(end = 6.dp)
                    )
                    Surface(
                        color = if (isHighResMode) Color(0xFF0284C7) else Color.White.copy(alpha = 0.18f),
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier
                            .clip(RoundedCornerShape(14.dp))
                            .clickable {
                                val next = !isHighResMode
                                isHighResMode = next
                                val label = if (next) "保存解像度: フル解像度" else "保存解像度: 標準 2048px (高速・省メモリ)"
                                Toast.makeText(context, label, Toast.LENGTH_SHORT).show()
                            }
                            .testTag("camerax_resolution_toggle_button")
                    ) {
                        Text(
                            text = if (isHighResMode) "フル解像度" else "標準 2048px",
                            color = Color.White,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 5.dp)
                        )
                    }
                }

                // 折りたたみ詳細設定バー (Camera2Interop 対応範囲のみ表示)
                AnimatedVisibility(visible = showProPanel && hasProSupport) {
                    Camera2ProControlsPanel(
                        caps = cam2Caps,
                        manualIso = manualIso,
                        manualShutterNanos = manualShutterNanos,
                        manualAwbMode = manualAwbMode,
                        onIsoChange = { manualIso = it },
                        onShutterChange = { manualShutterNanos = it },
                        onAwbChange = { manualAwbMode = it },
                        onResetAll = {
                            manualIso = null
                            manualShutterNanos = null
                            manualAwbMode = null
                        }
                    )
                }
            }

            // 6. 下部操作バー (倍率表示 + 明るさEVスライダー + サムネ/シャッター/完了)
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .align(Alignment.BottomCenter)
                    .background(Color.Black.copy(alpha = 0.62f))
                    .navigationBarsPadding()
                    .padding(start = 16.dp, end = 16.dp, top = 10.dp, bottom = 14.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                // ズーム倍率 & レンズ選択ピル
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.Center,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    val zoomPresets = remember(minZoom, maxZoom) {
                        buildList {
                            if (minZoom <= 0.7f) add(0.6f)
                            add(1.0f)
                            if (maxZoom >= 2.0f) add(2.0f)
                        }
                    }
                    zoomPresets.forEach { preset ->
                        val active = kotlin.math.abs(currentZoom - preset) < 0.12f
                        Box(
                            modifier = Modifier
                                .defaultMinSize(minWidth = 48.dp, minHeight = 48.dp)
                                .clickable {
                                    activeCamera?.cameraControl?.setZoomRatio(preset.coerceIn(minZoom, maxZoom))
                                }
                                .padding(horizontal = 4.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            Surface(
                                color = if (active) Color(0xFFFACC15) else Color.White.copy(alpha = 0.16f),
                                shape = RoundedCornerShape(14.dp)
                            ) {
                                Text(
                                    text = String.format(Locale.US, "%.1fx", preset),
                                    color = if (active) Color.Black else Color.White,
                                    fontSize = 11.sp,
                                    fontWeight = FontWeight.Bold,
                                    modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp)
                                )
                            }
                        }
                    }
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = String.format(Locale.US, "現在 %.1fx", currentZoom),
                        color = Color.White.copy(alpha = 0.85f),
                        fontSize = 11.sp
                    )
                }

                // 露出補正 (明るさ EV) スライダー
                if (evSupported && evUpper > evLower && manualIso == null && manualShutterNanos == null) {
                    val evValue = evIndex * evStep
                    val evFormatted = String.format(Locale.US, "%+.1f EV", evValue)
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 4.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "明るさ",
                            color = Color.White,
                            fontSize = 11.sp,
                            fontWeight = FontWeight.SemiBold,
                            modifier = Modifier.width(44.dp)
                        )
                        Slider(
                            value = evIndex.toFloat(),
                            onValueChange = { raw ->
                                val idx = raw.roundToInt().coerceIn(evLower, evUpper)
                                if (idx != evIndex) {
                                    evIndex = idx
                                    activeCamera?.cameraControl?.setExposureCompensationIndex(idx)
                                }
                            },
                            valueRange = evLower.toFloat()..evUpper.toFloat(),
                            colors = SliderDefaults.colors(
                                thumbColor = Color(0xFFFACC15),
                                activeTrackColor = Color(0xFFFACC15),
                                inactiveTrackColor = Color.White.copy(alpha = 0.3f)
                            ),
                            modifier = Modifier
                                .weight(1f)
                                .height(32.dp)
                                .testTag("camerax_ev_slider")
                        )
                        Text(
                            text = if (evIndex == 0) "0.0 EV" else evFormatted,
                            color = if (evIndex == 0) Color.White else Color(0xFFFACC15),
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            modifier = Modifier
                                .width(58.dp)
                                .clickable {
                                    evIndex = 0
                                    activeCamera?.cameraControl?.setExposureCompensationIndex(0)
                                }
                                .padding(start = 6.dp)
                        )
                    }
                }

                Spacer(modifier = Modifier.height(8.dp))

                // 最下部アクション行：左 (直前プレビュー＆取消) / 中央 (シャッター) / 右 (完了)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // 左側：直前撮影サムネイル＋枚数＋1枚取消
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.weight(1f)
                    ) {
                        val latestPath = capturedPaths.lastOrNull()
                        if (latestPath != null) {
                            Box(
                                modifier = Modifier
                                    .size(48.dp)
                                    .clip(RoundedCornerShape(8.dp))
                                    .border(1.5.dp, Color.White, RoundedCornerShape(8.dp))
                                    .clickable {
                                        if (capturedPaths.isNotEmpty()) {
                                            previewCapturedIndex = capturedPaths.lastIndex
                                        }
                                    }
                                    .testTag("camerax_thumbnail_preview_button")
                            ) {
                                AsyncImage(
                                    model = remember(latestPath) {
                                        ImageRequest.Builder(context)
                                            .data(File(latestPath))
                                            .size(360)
                                            .precision(Precision.INEXACT)
                                            .build()
                                    },
                                    contentDescription = "直前の撮影写真を拡大プレビュー・選別",
                                    contentScale = ContentScale.Crop,
                                    modifier = Modifier.fillMaxSize()
                                )
                                Surface(
                                    color = MaterialTheme.colorScheme.primary,
                                    shape = CircleShape,
                                    modifier = Modifier
                                        .align(Alignment.TopEnd)
                                        .padding(2.dp)
                                ) {
                                    Text(
                                        text = "${capturedPaths.size}",
                                        color = Color.White,
                                        fontSize = 10.sp,
                                        fontWeight = FontWeight.Bold,
                                        modifier = Modifier.padding(horizontal = 5.dp, vertical = 1.dp)
                                    )
                                }
                            }
                            IconButton(
                                onClick = {
                                    val removed = capturedPaths.lastOrNull()
                                    if (removed != null) {
                                        capturedPaths = capturedPaths.dropLast(1)
                                        try {
                                            File(removed).delete()
                                        } catch (_: Exception) {
                                        }
                                    }
                                },
                                modifier = Modifier
                                    .size(48.dp)
                                    .testTag("camerax_undo_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Undo,
                                    contentDescription = "直前の1枚を取り消す",
                                    tint = Color.White
                                )
                            }
                        } else {
                            Text(
                                text = "タップでピント",
                                color = Color.White.copy(alpha = 0.7f),
                                fontSize = 11.sp
                            )
                        }
                    }

                    // 中央：シャッターボタン
                    Box(
                        contentAlignment = Alignment.Center,
                        modifier = Modifier
                            .size(72.dp)
                            .clip(CircleShape)
                            .border(4.dp, Color.White, CircleShape)
                            .padding(6.dp)
                            .clip(CircleShape)
                            .background(if (isCapturing) Color.LightGray else Color.White)
                            .clickable(enabled = !isCapturing && imageCapture != null) {
                                val captureUseCase = imageCapture ?: return@clickable
                                val outFile = storageManager.createTempCameraPhotoFile(
                                    itemTitle.ifBlank { "Unassigned" }
                                )
                                if (outFile == null) {
                                    Toast.makeText(context, "保存先ファイルを作成できませんでした", Toast.LENGTH_SHORT).show()
                                    return@clickable
                                }
                                isCapturing = true
                                pendingTempFiles = pendingTempFiles + outFile.absolutePath
                                showShutterFlash = true
                                captureUseCase.targetRotation = previewViewRef?.display?.rotation ?: Surface.ROTATION_0
                                val outputOptions = ImageCapture.OutputFileOptions.Builder(outFile).build()
                                captureUseCase.takePicture(
                                    outputOptions,
                                    ContextCompat.getMainExecutor(context),
                                    object : ImageCapture.OnImageSavedCallback {
                                        override fun onImageSaved(outputFileResults: ImageCapture.OutputFileResults) {
                                            if (isHighResMode) {
                                                pendingTempFiles = pendingTempFiles - outFile.absolutePath
                                                capturedPaths = capturedPaths + outFile.absolutePath
                                                isCapturing = false
                                            } else {
                                                coroutineScope.launch {
                                                    try {
                                                        withContext(Dispatchers.IO) {
                                                            storageManager.downscaleCapturedPhotoIfNeeded(
                                                                file = outFile,
                                                                maxDim = 2048,
                                                                quality = 90
                                                            )
                                                        }
                                                        pendingTempFiles = pendingTempFiles - outFile.absolutePath
                                                        capturedPaths = capturedPaths + outFile.absolutePath
                                                    } catch (e: Exception) {
                                                        pendingTempFiles = pendingTempFiles - outFile.absolutePath
                                                        withContext(Dispatchers.IO + NonCancellable) {
                                                            try { outFile.delete() } catch (_: Exception) {}
                                                        }
                                                    } finally {
                                                        isCapturing = false
                                                    }
                                                }
                                            }
                                        }

                                        override fun onError(exception: ImageCaptureException) {
                                            pendingTempFiles = pendingTempFiles - outFile.absolutePath
                                            isCapturing = false
                                            try {
                                                outFile.delete()
                                            } catch (_: Exception) {
                                            }
                                            Toast.makeText(
                                                context,
                                                "撮影に失敗しました: ${exception.message ?: "エラー"}",
                                                Toast.LENGTH_SHORT
                                            ).show()
                                        }
                                    }
                                )
                            }
                            .testTag("camerax_shutter_button")
                    ) {
                        if (isCapturing) {
                            CircularProgressIndicator(
                                strokeWidth = 2.5.dp,
                                color = Color.Black,
                                modifier = Modifier.size(28.dp)
                            )
                        }
                    }

                    // 右側：完了ボタン
                    Box(
                        modifier = Modifier.weight(1f),
                        contentAlignment = Alignment.CenterEnd
                    ) {
                        Button(
                            enabled = capturedPaths.isNotEmpty() && !isCapturing,
                            onClick = {
                                purgePendingDiscards()
                                onPhotosCaptured(capturedPaths)
                            },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = MaterialTheme.colorScheme.primary,
                                disabledContainerColor = Color.White.copy(alpha = 0.16f)
                            ),
                            shape = RoundedCornerShape(24.dp),
                            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp),
                            modifier = Modifier
                                .height(48.dp)
                                .semantics(mergeDescendants = true) {}
                                .testTag("camerax_done_button")
                        ) {
                            Icon(
                                imageVector = Icons.Default.Check,
                                contentDescription = null,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(4.dp))
                            Text(
                                text = if (capturedPaths.isEmpty()) "完了" else "完了(${capturedPaths.size})",
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                maxLines = 1
                            )
                        }
                    }
                }
            }

            val openPreviewIdx = previewCapturedIndex
            if (openPreviewIdx != null && capturedPaths.isNotEmpty()) {
                val previewItems = remember(capturedPaths) {
                    capturedPaths.map { path ->
                        PreviewPhotoItem(model = File(path), hasLocalOriginal = true)
                    }
                }
                PhotoPreviewDialog(
                    photos = previewItems,
                    initialIndex = openPreviewIdx.coerceIn(0, previewItems.lastIndex),
                    isPcServerConnected = false,
                    confirmBeforeDelete = false,
                    onDeletePhoto = { deleteIdx ->
                        if (deleteIdx in capturedPaths.indices) {
                            val removedPath = capturedPaths[deleteIdx]
                            lastDeletedShot = deleteIdx to removedPath
                            pendingDiscardPaths = pendingDiscardPaths + removedPath
                            val updated = capturedPaths.toMutableList().apply { removeAt(deleteIdx) }
                            capturedPaths = updated
                            if (updated.isEmpty()) {
                                previewCapturedIndex = null
                            }
                        }
                    },
                    onUndoDelete = {
                        val deleted = lastDeletedShot ?: return@PhotoPreviewDialog
                        val (restoreIdx, restorePath) = deleted
                        lastDeletedShot = null
                        if (File(restorePath).exists()) {
                            pendingDiscardPaths = pendingDiscardPaths - restorePath
                            val updated = capturedPaths.toMutableList()
                            updated.add(restoreIdx.coerceIn(0, updated.size), restorePath)
                            capturedPaths = updated
                        }
                    },
                    onDismiss = { previewCapturedIndex = null }
                )
            }
        }
}
