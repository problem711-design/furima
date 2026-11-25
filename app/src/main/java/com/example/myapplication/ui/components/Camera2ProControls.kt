package com.example.myapplication.ui.components

import android.content.Context
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraMetadata
import android.hardware.camera2.CaptureRequest
import android.util.Range
import androidx.annotation.OptIn
import androidx.camera.camera2.interop.Camera2CameraControl
import androidx.camera.camera2.interop.Camera2CameraInfo
import androidx.camera.camera2.interop.CaptureRequestOptions
import androidx.camera.camera2.interop.ExperimentalCamera2Interop
import androidx.camera.core.Camera
import androidx.camera.core.CameraInfo
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageCapture
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.roundToInt

internal enum class CameraGuideMode {
    NONE,
    GRID_3X3,
    GRID_AND_SQUARE
}

internal data class CameraLensOption(
    val cameraId: String,
    val lensFacing: Int,
    val label: String
)

internal data class Camera2Capabilities(
    val supportsManualSensor: Boolean = false,
    val isoRange: Range<Int>? = null,
    val exposureTimeRange: Range<Long>? = null,
    val availableAwbModes: List<Int> = emptyList()
)

internal data class ShutterPreset(
    val label: String,
    val nanos: Long
)

internal val SHUTTER_PRESETS = listOf(
    ShutterPreset("1/1000", 1_000_000L),
    ShutterPreset("1/500", 2_000_000L),
    ShutterPreset("1/250", 4_000_000L),
    ShutterPreset("1/125", 8_000_000L),
    ShutterPreset("1/60", 16_666_667L),
    ShutterPreset("1/30", 33_333_333L),
    ShutterPreset("1/15", 66_666_667L)
)

@Composable
internal fun Camera2ProControlsPanel(
    caps: Camera2Capabilities,
    manualIso: Int?,
    manualShutterNanos: Long?,
    manualAwbMode: Int?,
    onIsoChange: (Int?) -> Unit,
    onShutterChange: (Long?) -> Unit,
    onAwbChange: (Int?) -> Unit,
    onResetAll: () -> Unit
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 4.dp)
            .background(Color.Black.copy(alpha = 0.65f), RoundedCornerShape(10.dp))
            .padding(horizontal = 10.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = "簡易マニュアル調整 (対応項目のみ表示)",
                color = Color(0xFFFACC15),
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold
            )
            TextButton(
                onClick = onResetAll,
                contentPadding = PaddingValues(horizontal = 8.dp, vertical = 0.dp),
                modifier = Modifier.height(32.dp)
            ) {
                Text("Auto戻し", color = Color.White, fontSize = 11.sp)
            }
        }

        // ISO 感度
        val isoRange = caps.isoRange
        if (caps.supportsManualSensor && isoRange != null) {
            val minIso = isoRange.lower.coerceAtLeast(50)
            val maxIso = isoRange.upper.coerceAtMost(3200).coerceAtLeast(minIso)
            val currentIsoVal = manualIso ?: minIso
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = if (manualIso == null) "ISO: Auto" else "ISO: $manualIso",
                    color = Color.White,
                    fontSize = 11.sp,
                    modifier = Modifier.width(74.dp)
                )
                Slider(
                    value = currentIsoVal.toFloat(),
                    onValueChange = { v -> onIsoChange(((v / 50f).roundToInt() * 50).coerceIn(minIso, maxIso)) },
                    valueRange = minIso.toFloat()..maxIso.toFloat(),
                    colors = SliderDefaults.colors(
                        thumbColor = Color(0xFFFACC15),
                        activeTrackColor = Color(0xFFFACC15)
                    ),
                    modifier = Modifier
                        .weight(1f)
                        .height(28.dp)
                )
            }
        }

        // シャッター速度
        val expRange = caps.exposureTimeRange
        if (caps.supportsManualSensor && expRange != null) {
            val usablePresets = remember(expRange) {
                SHUTTER_PRESETS.filter { it.nanos in expRange.lower..expRange.upper }
            }
            if (usablePresets.isNotEmpty()) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text("SS:", color = Color.White, fontSize = 11.sp)
                    FilterChip(
                        selected = manualShutterNanos == null,
                        onClick = { onShutterChange(null) },
                        label = { Text("Auto", fontSize = 10.sp) },
                        colors = FilterChipDefaults.filterChipColors(
                            labelColor = Color.White,
                            selectedLabelColor = Color.Black,
                            selectedContainerColor = Color(0xFFFACC15)
                        )
                    )
                    usablePresets.forEach { preset ->
                        FilterChip(
                            selected = manualShutterNanos == preset.nanos,
                            onClick = { onShutterChange(preset.nanos) },
                            label = { Text(preset.label, fontSize = 10.sp) },
                            colors = FilterChipDefaults.filterChipColors(
                                labelColor = Color.White,
                                selectedLabelColor = Color.Black,
                                selectedContainerColor = Color(0xFFFACC15)
                            )
                        )
                    }
                }
            }
        }

        // ホワイトバランス (WB)
        val wbOptions = remember(caps.availableAwbModes) {
            listOf(
                CameraMetadata.CONTROL_AWB_MODE_AUTO to "WB Auto",
                CameraMetadata.CONTROL_AWB_MODE_DAYLIGHT to "太陽光",
                CameraMetadata.CONTROL_AWB_MODE_CLOUDY_DAYLIGHT to "曇天",
                CameraMetadata.CONTROL_AWB_MODE_FLUORESCENT to "蛍光灯",
                CameraMetadata.CONTROL_AWB_MODE_INCANDESCENT to "電球"
            ).filter { caps.availableAwbModes.contains(it.first) }
        }
        if (wbOptions.size > 1) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                wbOptions.forEach { (mode, label) ->
                    val selected = (manualAwbMode ?: CameraMetadata.CONTROL_AWB_MODE_AUTO) == mode
                    FilterChip(
                        selected = selected,
                        onClick = {
                            onAwbChange(if (mode == CameraMetadata.CONTROL_AWB_MODE_AUTO) null else mode)
                        },
                        label = { Text(label, fontSize = 10.sp) },
                        colors = FilterChipDefaults.filterChipColors(
                            labelColor = Color.White,
                            selectedLabelColor = Color.Black,
                            selectedContainerColor = Color(0xFF38BDF8)
                        )
                    )
                }
            }
        }
    }
}

@OptIn(ExperimentalCamera2Interop::class)
internal fun discoverCameraLenses(provider: ProcessCameraProvider): List<CameraLensOption> {
    val result = mutableListOf<CameraLensOption>()
    var backCount = 0
    for (info in provider.availableCameraInfos) {
        try {
            val cam2 = Camera2CameraInfo.from(info)
            val id = cam2.cameraId
            val facing = cam2.getCameraCharacteristic(CameraCharacteristics.LENS_FACING) ?: continue
            val label = when (facing) {
                CameraCharacteristics.LENS_FACING_FRONT -> "前面"
                CameraCharacteristics.LENS_FACING_BACK -> {
                    backCount++
                    if (backCount == 1) "背面" else "背面$backCount"
                }
                else -> "その他"
            }
            result.add(CameraLensOption(cameraId = id, lensFacing = facing, label = label))
        } catch (_: Exception) {
        }
    }
    return result
}

@OptIn(ExperimentalCamera2Interop::class)
internal fun buildCameraSelector(selectedCameraId: String?): CameraSelector {
    if (selectedCameraId.isNullOrBlank()) return CameraSelector.DEFAULT_BACK_CAMERA
    return CameraSelector.Builder()
        .addCameraFilter { infos ->
            infos.filter {
                try {
                    Camera2CameraInfo.from(it).cameraId == selectedCameraId
                } catch (_: Exception) {
                    false
                }
            }.ifEmpty { listOfNotNull(infos.firstOrNull()) }
        }
        .build()
}

@OptIn(ExperimentalCamera2Interop::class)
internal fun readCamera2Capabilities(info: CameraInfo): Camera2Capabilities {
    return try {
        val cam2 = Camera2CameraInfo.from(info)
        val caps = cam2.getCameraCharacteristic(CameraCharacteristics.REQUEST_AVAILABLE_CAPABILITIES)
        val supportsManual = caps?.contains(CameraCharacteristics.REQUEST_AVAILABLE_CAPABILITIES_MANUAL_SENSOR) == true
        val isoRange = cam2.getCameraCharacteristic(CameraCharacteristics.SENSOR_INFO_SENSITIVITY_RANGE)
        val expRange = cam2.getCameraCharacteristic(CameraCharacteristics.SENSOR_INFO_EXPOSURE_TIME_RANGE)
        val awbModes = cam2.getCameraCharacteristic(CameraCharacteristics.CONTROL_AWB_AVAILABLE_MODES)?.toList() ?: emptyList()
        Camera2Capabilities(
            supportsManualSensor = supportsManual,
            isoRange = isoRange,
            exposureTimeRange = expRange,
            availableAwbModes = awbModes
        )
    } catch (_: Exception) {
        Camera2Capabilities()
    }
}

@OptIn(ExperimentalCamera2Interop::class)
internal fun applyCamera2ManualControls(
    camera: Camera,
    caps: Camera2Capabilities,
    manualIso: Int?,
    manualShutterNanos: Long?,
    manualAwbMode: Int?
) {
    try {
        val cam2Control = Camera2CameraControl.from(camera.cameraControl)
        if (manualIso == null && manualShutterNanos == null && manualAwbMode == null) {
            cam2Control.clearCaptureRequestOptions()
            return
        }
        val builder = CaptureRequestOptions.Builder()
        if (caps.supportsManualSensor && (manualIso != null || manualShutterNanos != null)) {
            builder.setCaptureRequestOption(CaptureRequest.CONTROL_AE_MODE, CameraMetadata.CONTROL_AE_MODE_OFF)
            val safeIso = manualIso ?: (caps.isoRange?.clamp(200) ?: 200)
            val safeExp = manualShutterNanos ?: (caps.exposureTimeRange?.clamp(16_666_667L) ?: 16_666_667L)
            builder.setCaptureRequestOption(CaptureRequest.SENSOR_SENSITIVITY, safeIso)
            builder.setCaptureRequestOption(CaptureRequest.SENSOR_EXPOSURE_TIME, safeExp)
        } else {
            builder.setCaptureRequestOption(CaptureRequest.CONTROL_AE_MODE, CameraMetadata.CONTROL_AE_MODE_ON)
        }
        if (manualAwbMode != null) {
            builder.setCaptureRequestOption(CaptureRequest.CONTROL_AWB_MODE, manualAwbMode)
        }
        cam2Control.setCaptureRequestOptions(builder.build())
    } catch (_: Exception) {
    }
}

internal data class SavedCameraSettings(
    val selectedCameraId: String? = null,
    val flashMode: Int = ImageCapture.FLASH_MODE_OFF,
    val guideModeOrdinal: Int = CameraGuideMode.GRID_3X3.ordinal,
    val zoomRatio: Float = 1f,
    val evIndex: Int = 0,
    val showProPanel: Boolean = false,
    val manualIso: Int? = null,
    val manualShutterNanos: Long? = null,
    val manualAwbMode: Int? = null,
    val isHighResMode: Boolean = false
)

internal object CameraXCapturePreferences {
    private const val PREFS_NAME = "camerax_capture_prefs"
    private const val KEY_CAMERA_ID = "selected_camera_id"
    private const val KEY_FLASH_MODE = "flash_mode"
    private const val KEY_GUIDE_MODE = "guide_mode_ordinal"
    private const val KEY_ZOOM_RATIO = "zoom_ratio"
    private const val KEY_EV_INDEX = "ev_index"
    private const val KEY_SHOW_PRO_PANEL = "show_pro_panel"
    private const val KEY_MANUAL_ISO = "manual_iso"
    private const val KEY_MANUAL_SHUTTER_NANOS = "manual_shutter_nanos"
    private const val KEY_MANUAL_AWB_MODE = "manual_awb_mode"
    private const val KEY_HIGH_RES_MODE = "high_res_mode"

    fun load(context: Context): SavedCameraSettings {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val rawFlash = prefs.getInt(KEY_FLASH_MODE, ImageCapture.FLASH_MODE_OFF)
        val safeFlash = when (rawFlash) {
            ImageCapture.FLASH_MODE_ON,
            ImageCapture.FLASH_MODE_AUTO,
            ImageCapture.FLASH_MODE_OFF -> rawFlash
            else -> ImageCapture.FLASH_MODE_OFF
        }
        val rawGuide = prefs.getInt(KEY_GUIDE_MODE, CameraGuideMode.GRID_3X3.ordinal)
        val safeGuide = rawGuide.coerceIn(0, CameraGuideMode.entries.lastIndex)
        val rawZoom = prefs.getFloat(KEY_ZOOM_RATIO, 1f)
        val safeZoom = if (rawZoom.isFinite() && rawZoom > 0f) rawZoom else 1f
        val isoVal = prefs.getInt(KEY_MANUAL_ISO, -1)
        val shutterVal = prefs.getLong(KEY_MANUAL_SHUTTER_NANOS, -1L)
        val awbVal = prefs.getInt(KEY_MANUAL_AWB_MODE, -1)

        return SavedCameraSettings(
            selectedCameraId = prefs.getString(KEY_CAMERA_ID, null),
            flashMode = safeFlash,
            guideModeOrdinal = safeGuide,
            zoomRatio = safeZoom,
            evIndex = prefs.getInt(KEY_EV_INDEX, 0),
            showProPanel = prefs.getBoolean(KEY_SHOW_PRO_PANEL, false),
            manualIso = if (isoVal > 0) isoVal else null,
            manualShutterNanos = if (shutterVal > 0L) shutterVal else null,
            manualAwbMode = if (awbVal >= 0) awbVal else null,
            isHighResMode = prefs.getBoolean(KEY_HIGH_RES_MODE, false)
        )
    }

    fun save(context: Context, settings: SavedCameraSettings) {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        prefs.edit().apply {
            if (settings.selectedCameraId.isNullOrBlank()) {
                remove(KEY_CAMERA_ID)
            } else {
                putString(KEY_CAMERA_ID, settings.selectedCameraId)
            }
            putInt(KEY_FLASH_MODE, settings.flashMode)
            putInt(KEY_GUIDE_MODE, settings.guideModeOrdinal)
            putFloat(KEY_ZOOM_RATIO, settings.zoomRatio)
            putInt(KEY_EV_INDEX, settings.evIndex)
            putBoolean(KEY_SHOW_PRO_PANEL, settings.showProPanel)
            putInt(KEY_MANUAL_ISO, settings.manualIso ?: -1)
            putLong(KEY_MANUAL_SHUTTER_NANOS, settings.manualShutterNanos ?: -1L)
            putInt(KEY_MANUAL_AWB_MODE, settings.manualAwbMode ?: -1)
            putBoolean(KEY_HIGH_RES_MODE, settings.isHighResMode)
            apply()
        }
    }
}
