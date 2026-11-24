package com.example.myapplication.ui.screens

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.MediaStore
import android.provider.Settings
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.AddAPhoto
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.PhotoLibrary
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import coil.compose.AsyncImage
import coil.request.ImageRequest
import coil.size.Precision
import com.example.myapplication.data.PhotoStorageManager
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.isSoldFamilyStatus
import com.example.myapplication.data.local.PhotoEntity
import com.example.myapplication.ui.FleaMarketViewModel
import com.example.myapplication.data.server.ClaudeJobDto
import com.example.myapplication.ui.components.CameraXCaptureDialog
import com.example.myapplication.ui.components.ClaudeJobCancelDialog
import com.example.myapplication.ui.components.ClaudeSendDialog
import com.example.myapplication.ui.components.ListingValidationBanner
import com.example.myapplication.ui.components.evaluateListingWarnings
import com.example.myapplication.ui.components.PhotoPreviewDialog
import com.example.myapplication.ui.components.PreviewPhotoItem
import com.example.myapplication.ui.components.toPreviewPhotoItem
import java.io.File
import java.text.NumberFormat
import java.util.Locale

private sealed class DeletedEditPhoto(open val index: Int) {
    data class Existing(override val index: Int, val path: String) : DeletedEditPhoto(index)
    data class NewUri(override val index: Int, val uri: Uri) : DeletedEditPhoto(index)
}

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class, ExperimentalFoundationApi::class)
@Composable
fun ItemEditScreen(
    itemId: Int,
    viewModel: FleaMarketViewModel,
    onNavigateBack: () -> Unit
) {
    val context = LocalContext.current
    val defaultSettings by viewModel.defaultSettings.collectAsState()
    val isPcServerConnected by viewModel.isPcServerConnected.collectAsState()
    val lastSyncSuccessAt by viewModel.lastSyncSuccessAt.collectAsState()
    // フェーズ 6-2 (6-2-7): 編集画面の承認待ちの帯表示用
    val pendingClaudeApprovals by viewModel.pendingClaudeApprovals.collectAsState()

    var titleFieldValue by remember { mutableStateOf(TextFieldValue("")) }
    var descriptionFieldValue by remember { mutableStateOf(TextFieldValue("")) }
    val title = titleFieldValue.text
    val description = descriptionFieldValue.text
    var priceText by remember { mutableStateOf("") }
    var soldPriceText by remember { mutableStateOf("") }
    var status by remember { mutableStateOf("DRAFT") }

    // 費用設定
    var useCustomSettings by remember { mutableStateOf(false) }
    var feeRatePercentText by remember { mutableStateOf("") }
    var shippingCostText by remember { mutableStateOf("") }
    var expenseCostText by remember { mutableStateOf("") }

    val newImageUris = remember { mutableStateListOf<Uri>() }
    val existingFilePaths = remember { mutableStateListOf<String>() }
    val createdTempCameraPaths = remember { mutableStateListOf<String>() }

    var cameraPhotoUri by remember { mutableStateOf<Uri?>(null) }
    var tempCameraPath by rememberSaveable { mutableStateOf<String?>(null) }
    var showCameraXDialog by rememberSaveable { mutableStateOf(false) }
    var showCameraPermissionDeniedDialog by rememberSaveable { mutableStateOf(false) }

    var initialTitle by remember(itemId) { mutableStateOf("") }
    var initialDescription by remember(itemId) { mutableStateOf("") }
    var initialPriceText by remember(itemId) { mutableStateOf("") }
    var initialSoldPriceText by remember(itemId) { mutableStateOf("") }
    var initialStatus by remember(itemId) { mutableStateOf("DRAFT") }
    var initialUseCustomSettings by remember(itemId) { mutableStateOf(false) }
    var initialFeeRatePercentText by remember(itemId) { mutableStateOf("") }
    var initialShippingCostText by remember(itemId) { mutableStateOf("") }
    var initialExpenseCostText by remember(itemId) { mutableStateOf("") }
    var initialExistingFilePaths by remember(itemId) { mutableStateOf<List<String>>(emptyList()) }
    val loadedPhotosByPath = remember(itemId) { mutableStateMapOf<String, PhotoEntity>() }
    var previewStartIndex by remember(itemId) { mutableStateOf<Int?>(null) }
    var lastDeletedPreviewPhoto by remember(itemId) { mutableStateOf<DeletedEditPhoto?>(null) }
    var isInitialized by remember(itemId) { mutableStateOf(false) }
    var showDiscardDialog by remember { mutableStateOf(false) }
    var isSaving by remember { mutableStateOf(false) }

    // フェーズ 5-3: サムネイル複数選択 (一括削除 / Museに送る)
    var loadedItem by remember(itemId) { mutableStateOf<ItemEntity?>(null) }
    var photoSelectionMode by remember(itemId) { mutableStateOf(false) }
    val selectedPhotoKeys = remember(itemId) { mutableStateListOf<String>() }
    var showBulkDeleteConfirmDialog by remember { mutableStateOf(false) }
    var showClaudeSendDialog by remember { mutableStateOf(false) }
    var isSendingToClaude by remember { mutableStateOf(false) }

    val pickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.PickMultipleVisualMedia()
    ) { uris ->
        newImageUris.addAll(uris)
    }

    val cameraLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.TakePicture()
    ) { success ->
        val path = tempCameraPath
        if (success && path != null) {
            existingFilePaths.add(path)
            createdTempCameraPaths.add(path)
        } else if (path != null) {
            try {
                File(path).delete()
            } catch (_: Exception) {}
        }
        tempCameraPath = null
    }

    val cameraPermissionLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            showCameraXDialog = true
        } else {
            showCameraPermissionDeniedDialog = true
        }
    }

    // フェーズ 6-5 (是正 Build 8): 加工ラベル表示のためのジョブ状態購読と閲覧セッション開始
    // フェーズ 6-10 (§37.4): ラベルチップ / プレビューからの取消対象 (ジョブと、その写真の md5)
    var claudeCancelTarget by remember(itemId) { mutableStateOf<Pair<ClaudeJobDto, String>?>(null) }
    val claudeJobsForLabels by viewModel.claudeJobsForLabels.collectAsState()
    val claudeLabelSeenAt by viewModel.claudeLabelSeenAt.collectAsState()
    val labelVisitSnapshot by viewModel.labelVisitSnapshot.collectAsState()
    val labelBundleUuids by viewModel.labelBundleUuids.collectAsState()
    LaunchedEffect(loadedItem?.syncUuid) {
        loadedItem?.let { item -> if (item.syncUuid.isNotBlank()) viewModel.beginLabelVisit(item.id, item.syncUuid) }
    }

    LaunchedEffect(itemId) {
        if (itemId != 0) {
            val itemWithPhotos = viewModel.getItemById(itemId)
            if (itemWithPhotos != null) {
                val item = itemWithPhotos.item
                loadedItem = item
                titleFieldValue = TextFieldValue(item.title, TextRange(item.title.length))
                descriptionFieldValue = TextFieldValue(item.description, TextRange(item.description.length))
                priceText = if (item.price > 0) item.price.toString() else ""
                soldPriceText = if (item.soldPrice > 0) item.soldPrice.toString() else ""
                status = item.status
                useCustomSettings = item.useCustomSettings
                feeRatePercentText = item.feeRatePercent.toString()
                shippingCostText = item.shippingCost.toString()
                expenseCostText = item.expenseCost.toString()
                existingFilePaths.clear()
                loadedPhotosByPath.clear()
                val resolvedPairs = withContext(Dispatchers.IO) {
                    itemWithPhotos.photos.mapNotNull { photo ->
                        val displayPath = photo.displayFile?.absolutePath ?: photo.filePath
                        val targetPath = if (File(photo.filePath).exists()) photo.filePath else displayPath
                        if (targetPath.isNotBlank()) targetPath to photo else null
                    }
                }
                resolvedPairs.forEach { (targetPath, photo) ->
                    existingFilePaths.add(targetPath)
                    loadedPhotosByPath[targetPath] = photo
                }
            }
        } else {
            feeRatePercentText = defaultSettings.defaultFeeRatePercent.toString()
            shippingCostText = defaultSettings.defaultShippingCost.toString()
            expenseCostText = defaultSettings.defaultExpenseCost.toString()
        }
        initialTitle = titleFieldValue.text
        initialDescription = descriptionFieldValue.text
        initialPriceText = priceText
        initialSoldPriceText = soldPriceText
        initialStatus = status
        initialUseCustomSettings = useCustomSettings
        initialFeeRatePercentText = feeRatePercentText
        initialShippingCostText = shippingCostText
        initialExpenseCostText = expenseCostText
        initialExistingFilePaths = existingFilePaths.toList()
        isInitialized = true
    }

    DisposableEffect(itemId) {
        if (itemId != 0) {
            viewModel.setItemEditingActive(itemId, true)
        }
        onDispose {
            if (itemId != 0) {
                viewModel.setItemEditingActive(itemId, false)
            }
        }
    }

    // Build 13 (13-5) ＆ Build 17: 同期完了時に写真エンティティと実体パスを再照合し、
    // Step 3 で原本からサムネイルパスへ切り替わった場合も画面側リストを追従させる。
    LaunchedEffect(lastSyncSuccessAt) {
        if (itemId != 0 && lastSyncSuccessAt != null && isInitialized) {
            val itemWithPhotos = viewModel.getItemById(itemId)
            if (itemWithPhotos != null) {
                loadedItem = itemWithPhotos.item
                val reloadedPhotos = itemWithPhotos.photos
                val existingSnapshot = existingFilePaths.toList()
                val loadedSnapshot = loadedPhotosByPath.toMap()
                val initSnapshot = initialExistingFilePaths.toList()
                val syncResolution = withContext(Dispatchers.IO) {
                    val photosById = reloadedPhotos.associateBy { it.id }

                    fun resolveMatchingPhoto(oldPath: String): PhotoEntity? {
                        val prevEntity = loadedSnapshot[oldPath]
                        if (prevEntity != null) {
                            photosById[prevEntity.id]?.let { return it }
                            if (prevEntity.md5Hash.isNotBlank()) {
                                reloadedPhotos.firstOrNull { it.md5Hash == prevEntity.md5Hash }?.let { return it }
                            }
                        }
                        val oldName = File(oldPath).name
                        val thumbName = "THUMB_$oldName"
                        return reloadedPhotos.firstOrNull {
                            it.filePath == oldPath ||
                                it.thumbnailPath == oldPath ||
                                File(it.filePath).name == thumbName ||
                                (it.thumbnailPath != null && File(it.thumbnailPath).name == oldName)
                        }
                    }

                    val pathReplacements = mutableMapOf<String, String>()
                    val updatedExisting = existingSnapshot.toMutableList()
                    val loadedUpdates = mutableMapOf<String, PhotoEntity>()
                    for (i in existingSnapshot.indices) {
                        val oldPath = existingSnapshot[i]
                        val matched = resolveMatchingPhoto(oldPath) ?: continue
                        val displayPath = matched.displayFile?.absolutePath ?: matched.filePath
                        val targetPath = if (File(matched.filePath).exists()) matched.filePath else displayPath
                        if (targetPath.isNotBlank()) {
                            loadedUpdates[targetPath] = matched
                            if (targetPath != oldPath) {
                                updatedExisting[i] = targetPath
                                pathReplacements[oldPath] = targetPath
                            }
                        }
                    }

                    reloadedPhotos.forEach { photo ->
                        val displayPath = photo.displayFile?.absolutePath ?: photo.filePath
                        val targetPath = if (File(photo.filePath).exists()) photo.filePath else displayPath
                        if (targetPath.isNotBlank()) {
                            loadedUpdates[targetPath] = photo
                        }
                    }

                    val updatedInit = initSnapshot.map { initPath ->
                        pathReplacements[initPath] ?: run {
                            val matched = resolveMatchingPhoto(initPath)
                            val displayPath = matched?.displayFile?.absolutePath ?: matched?.filePath
                            val targetPath = if (matched != null && File(matched.filePath).exists()) matched.filePath else displayPath
                            if (!targetPath.isNullOrBlank()) targetPath else initPath
                        }
                    }
                    Triple(updatedExisting, loadedUpdates, Pair(pathReplacements, updatedInit))
                }
                val (updatedExisting, loadedUpdates, pairRest) = syncResolution
                val (pathReplacements, updatedInit) = pairRest
                for (i in updatedExisting.indices) {
                    if (i < existingFilePaths.size && existingFilePaths[i] != updatedExisting[i]) {
                        existingFilePaths[i] = updatedExisting[i]
                    }
                }
                loadedPhotosByPath.putAll(loadedUpdates)
                initialExistingFilePaths = updatedInit

                if (pathReplacements.isNotEmpty()) {
                    for (idx in selectedPhotoKeys.indices) {
                        val key = selectedPhotoKeys[idx]
                        if (key.startsWith("path:")) {
                            val oldP = key.removePrefix("path:")
                            val newP = pathReplacements[oldP]
                            if (newP != null) {
                                selectedPhotoKeys[idx] = "path:$newP"
                            }
                        }
                    }
                    val deletedPreview = lastDeletedPreviewPhoto
                    if (deletedPreview is DeletedEditPhoto.Existing) {
                        val newP = pathReplacements[deletedPreview.path]
                        if (newP != null) {
                            lastDeletedPreviewPhoto = deletedPreview.copy(path = newP)
                        }
                    }
                    pathReplacements.keys.forEach { oldP ->
                        if (!existingFilePaths.contains(oldP)) {
                            loadedPhotosByPath.remove(oldP)
                        }
                    }
                }
            }
        }
    }

    val hasUnsavedChanges = isInitialized && (
        title != initialTitle ||
            description != initialDescription ||
            priceText != initialPriceText ||
            soldPriceText != initialSoldPriceText ||
            status != initialStatus ||
            useCustomSettings != initialUseCustomSettings ||
            feeRatePercentText != initialFeeRatePercentText ||
            shippingCostText != initialShippingCostText ||
            expenseCostText != initialExpenseCostText ||
            existingFilePaths.toList() != initialExistingFilePaths ||
            newImageUris.isNotEmpty()
        )

    // フェーズ 5-3: 選択キー (既存写真はパス、新規写真はURIで識別) と派生リスト
    fun togglePhotoSelection(key: String) {
        if (selectedPhotoKeys.contains(key)) {
            selectedPhotoKeys.remove(key)
        } else {
            selectedPhotoKeys.add(key)
        }
        if (selectedPhotoKeys.isEmpty()) {
            photoSelectionMode = false
        }
    }

    fun exitPhotoSelectionMode() {
        selectedPhotoKeys.clear()
        photoSelectionMode = false
    }

    val selectedExistingPaths = existingFilePaths.filter { selectedPhotoKeys.contains("path:$it") }
    val selectedNewUris = newImageUris.filter { selectedPhotoKeys.contains("uri:$it") }
    val selectedPhotoCount = selectedExistingPaths.size + selectedNewUris.size
    // Muse送信の対象は PC 同期済み (md5Hash 確定) の既存写真のみ (Q2)
    // フェーズ 6-14 (§48/§48.5): 送信順はタップ順 = selectedPhotoKeys の順序とする
    // (§46 の番号スタンプは受け取った順で振られるため、写真一覧の順序で組み立ててはならない)
    val claudeEligiblePhotos: List<Pair<String, String>> = selectedPhotoKeys.mapNotNull { key ->
        if (!key.startsWith("path:")) {
            null
        } else {
            val path = key.removePrefix("path:")
            val entity = loadedPhotosByPath[path]
            if (entity != null && entity.md5Hash.isNotBlank() && entity.isPcSynced) {
                entity.md5Hash to File(path).name
            } else {
                null
            }
        }
    }
    val claudeExcludedCount = selectedPhotoCount - claudeEligiblePhotos.size

    // フェーズ 6-9 是正 (Build 14 / 61-1): グリッド側ボタンとプレビュー側ボタンで共用する送信前ガード。
    // 検証を通過した場合のみ送信ダイアログを開き true を返す (未達時は既存と同じ Toast で案内する)。
    fun tryOpenClaudeSendDialog(): Boolean {
        return when {
            itemId == 0 || hasUnsavedChanges -> {
                Toast.makeText(context, "保存してからMuseに送ってください", Toast.LENGTH_LONG).show()
                false
            }
            (loadedItem?.mergedChildCount ?: 0) > 0 -> {
                Toast.makeText(context, "まとめ商品は個別の子商品から送信してください", Toast.LENGTH_LONG).show()
                false
            }
            claudeEligiblePhotos.isEmpty() -> {
                Toast.makeText(context, "PC同期済みの写真が選択されていません", Toast.LENGTH_LONG).show()
                false
            }
            isSendingToClaude -> false
            else -> {
                showClaudeSendDialog = true
                true
            }
        }
    }

    val handleBackRequest: () -> Unit = {
        if (!isSaving) {
            if (hasUnsavedChanges) {
                showDiscardDialog = true
            } else {
                onNavigateBack()
            }
        }
    }

    BackHandler(enabled = hasUnsavedChanges || isSaving) {
        if (!isSaving) {
            showDiscardDialog = true
        }
    }

    if (showBulkDeleteConfirmDialog) {
        AlertDialog(
            onDismissRequest = { showBulkDeleteConfirmDialog = false },
            title = { Text("写真の一括削除") },
            text = {
                Text("選択した ${selectedPhotoCount}枚の写真を編集リストから除外します。「保存する」を押すまで確定しません（取り消しはできません）。")
            },
            confirmButton = {
                Button(
                    onClick = {
                        existingFilePaths.removeAll(selectedExistingPaths.toSet())
                        newImageUris.removeAll(selectedNewUris.toSet())
                        showBulkDeleteConfirmDialog = false
                        exitPhotoSelectionMode()
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
                ) {
                    Text("除外する")
                }
            },
            dismissButton = {
                TextButton(onClick = { showBulkDeleteConfirmDialog = false }) {
                    Text("キャンセル")
                }
            }
        )
    }

    if (showClaudeSendDialog) {
        ClaudeSendDialog(
            templates = defaultSettings.promptTemplates,
            photoCount = claudeEligiblePhotos.size,
            excludedCount = claudeExcludedCount,
            isConnected = isPcServerConnected,
            freeTextHistory = viewModel.claudeFreeTextHistory(),
            onDismiss = { showClaudeSendDialog = false },
            onFetchReferences = { viewModel.fetchClaudeReferences() },
            referenceImageUrl = { md5, thumb -> viewModel.claudeReferenceImageUrl(md5, thumb) },
            onSendTemplate = { tpl, queue, reference, stamp ->
                showClaudeSendDialog = false
                isSendingToClaude = true
                viewModel.sendPhotosToClaude(
                    itemId = itemId,
                    itemUuid = loadedItem?.syncUuid ?: "",
                    photos = claudeEligiblePhotos,
                    templateId = tpl.id,
                    queue = queue,
                    referenceMd5 = reference?.md5 ?: "",
                    referenceFileName = reference?.displayName ?: "",
                    stampNumber = stamp,
                    onError = { msg ->
                        isSendingToClaude = false
                        Toast.makeText(context, "送信エラー: $msg", Toast.LENGTH_LONG).show()
                    },
                    onSuccess = {
                        isSendingToClaude = false
                        Toast.makeText(
                            context,
                            if (queue == "waiting") "待機リストへ送りました" else "Museジョブを作成しました（承認リクエストを送信しました）",
                            Toast.LENGTH_LONG
                        ).show()
                        exitPhotoSelectionMode()
                    }
                )
            },
            onSendCustom = { text, queue, registerName, reference, stamp ->
                showClaudeSendDialog = false
                isSendingToClaude = true
                viewModel.sendPhotosToClaude(
                    itemId = itemId,
                    itemUuid = loadedItem?.syncUuid ?: "",
                    photos = claudeEligiblePhotos,
                    templateId = "",
                    queue = queue,
                    promptText = text,
                    registerTemplateName = registerName,
                    referenceMd5 = reference?.md5 ?: "",
                    referenceFileName = reference?.displayName ?: "",
                    stampNumber = stamp,
                    onError = { msg ->
                        isSendingToClaude = false
                        Toast.makeText(context, "送信エラー: $msg", Toast.LENGTH_LONG).show()
                    },
                    onSuccess = { result ->
                        isSendingToClaude = false
                        var msg = if (queue == "waiting") "待機リストへ送りました" else "Museジョブを作成しました（承認リクエストを送信しました）"
                        if (result.templateRegistered) msg += "（テンプレートに登録しました）"
                        if (result.templateRegisterError != null) msg += "（テンプレート登録は失敗: ${result.templateRegisterError}）"
                        Toast.makeText(context, msg, Toast.LENGTH_LONG).show()
                        exitPhotoSelectionMode()
                    }
                )
            }
        )
    }

    if (showDiscardDialog) {
        AlertDialog(
            onDismissRequest = { showDiscardDialog = false },
            title = { Text("変更を保存せずに戻りますか？") },
            text = { Text("入力した内容や追加した写真は保存されません。変更を破棄して前の画面に戻りますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        createdTempCameraPaths.forEach { path ->
                            try {
                                val f = File(path)
                                if (f.exists()) f.delete()
                            } catch (_: Exception) {}
                        }
                        createdTempCameraPaths.clear()
                        showDiscardDialog = false
                        onNavigateBack()
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                    modifier = Modifier.testTag("confirm_discard_edit_button")
                ) {
                    Text("破棄して戻る")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { showDiscardDialog = false },
                    modifier = Modifier.testTag("cancel_discard_edit_button")
                ) {
                    Text("編集を続ける")
                }
            }
        )
    }

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)
    val inputPrice = priceText.toIntOrNull() ?: 0
    val inputSoldPrice = soldPriceText.toIntOrNull() ?: inputPrice
    val basePrice = if (isSoldFamilyStatus(status) && inputSoldPrice > 0) inputSoldPrice else inputPrice

    val effectiveFeePercent = if (useCustomSettings) {
        feeRatePercentText.toIntOrNull() ?: defaultSettings.defaultFeeRatePercent
    } else {
        defaultSettings.defaultFeeRatePercent
    }

    val effectiveShipping = if (useCustomSettings) {
        shippingCostText.toIntOrNull() ?: defaultSettings.defaultShippingCost
    } else {
        defaultSettings.defaultShippingCost
    }

    val effectiveExpense = if (useCustomSettings) {
        expenseCostText.toIntOrNull() ?: defaultSettings.defaultExpenseCost
    } else {
        defaultSettings.defaultExpenseCost
    }

    val calculatedFee = (basePrice * effectiveFeePercent) / 100
    val estimatedProfit = (basePrice - calculatedFee - effectiveShipping - effectiveExpense).coerceAtLeast(0)

    val startPreviewIdx = previewStartIndex
    if (startPreviewIdx != null) {
        val existingSnap = existingFilePaths.toList()
        val loadedSnap = loadedPhotosByPath.toMap()
        val newUrisSnap = newImageUris.toList()
        val previewItems by produceState<List<PreviewPhotoItem>>(emptyList(), existingSnap, loadedSnap, newUrisSnap) {
            value = withContext(Dispatchers.IO) {
                buildList {
                    existingSnap.forEach { path ->
                        val entity = loadedSnap[path]
                        if (entity != null) {
                            add(entity.toPreviewPhotoItem().copy(photoKey = "path:$path"))
                        } else {
                            add(PreviewPhotoItem(model = File(path), hasLocalOriginal = true, photoKey = "path:$path"))
                        }
                    }
                    newUrisSnap.forEach { uri ->
                        add(PreviewPhotoItem(model = uri, hasLocalOriginal = true, photoKey = "uri:$uri"))
                    }
                }
            }
        }
        if (previewItems.isNotEmpty()) {
            PhotoPreviewDialog(
                photos = previewItems,
                initialIndex = startPreviewIdx,
                isPcServerConnected = isPcServerConnected,
                onFindCachedOriginal = { hash -> viewModel.findCachedOriginalPhotoFile(hash) },
                onFetchOriginal = { hash -> viewModel.fetchOriginalPhotoCacheFile(hash) },
                confirmBeforeDelete = false,
                // フェーズ 6-9 (Build 14): プレビューとグリッドで Muse 送信用の選択状態を共有する
                selectionKeys = selectedPhotoKeys.toSet(),
                onToggleSelection = { key ->
                    // プレビューで最初の 1 枚を ON にしたらグリッド側の選択モードも立てる (§41.2)
                    if (!selectedPhotoKeys.contains(key)) photoSelectionMode = true
                    togglePhotoSelection(key)
                },
                selectedCount = selectedPhotoCount,
                onSendToClaude = {
                    // フェーズ 6-9 是正 (61-1): グリッド側と同じ送信前ガードを通す。通過時のみプレビューを閉じてダイアログを開く
                    if (tryOpenClaudeSendDialog()) {
                        previewStartIndex = null
                    }
                },
                onDeletePhoto = { deleteIdx ->
                    var removedKey: String? = null
                    if (deleteIdx in existingFilePaths.indices) {
                        val removedPath = existingFilePaths.removeAt(deleteIdx)
                        lastDeletedPreviewPhoto = DeletedEditPhoto.Existing(deleteIdx, removedPath)
                        removedKey = "path:$removedPath"
                    } else {
                        val newIdx = deleteIdx - existingFilePaths.size
                        if (newIdx in newImageUris.indices) {
                            val removedUri = newImageUris.removeAt(newIdx)
                            lastDeletedPreviewPhoto = DeletedEditPhoto.NewUri(newIdx, removedUri)
                            removedKey = "uri:$removedUri"
                        }
                    }
                    // フェーズ 6-9 (Build 14): プレビューで削除した写真は選択からも外す (元に戻しても選択は戻らない)
                    if (removedKey != null) {
                        selectedPhotoKeys.remove(removedKey)
                        if (selectedPhotoKeys.isEmpty()) photoSelectionMode = false
                    }
                    if (existingFilePaths.isEmpty() && newImageUris.isEmpty()) {
                        previewStartIndex = null
                    }
                },
                onUndoDelete = {
                    val deleted = lastDeletedPreviewPhoto ?: return@PhotoPreviewDialog
                    lastDeletedPreviewPhoto = null
                    when (deleted) {
                        is DeletedEditPhoto.Existing -> {
                            existingFilePaths.add(
                                deleted.index.coerceIn(0, existingFilePaths.size),
                                deleted.path
                            )
                        }
                        is DeletedEditPhoto.NewUri -> {
                            newImageUris.add(
                                deleted.index.coerceIn(0, newImageUris.size),
                                deleted.uri
                            )
                        }
                    }
                },
                onResolvePhotoLabel = { hash ->
                    viewModel.claudePhotoLabelKey(loadedItem?.syncUuid ?: "", labelBundleUuids, hash, claudeJobsForLabels, claudeLabelSeenAt, labelVisitSnapshot)
                },
                onResolveLabelSourceJob = { hash ->
                    viewModel.claudePhotoLabelSourceJob(loadedItem?.syncUuid ?: "", labelBundleUuids, hash, claudeJobsForLabels)
                },
                onCancelLabelJob = { job, hash -> claudeCancelTarget = job to hash },
                onDismiss = { previewStartIndex = null }
            )
        }
    }

    // フェーズ 6-10 (§37.4): ジョブ取消ダイアログ (グリッドのチップ / プレビュー共通)
    claudeCancelTarget?.let { (job, md5) ->
        ClaudeJobCancelDialog(
            job = job,
            onConfirm = {
                claudeCancelTarget = null
                viewModel.cancelClaudeJobFromLabel(loadedItem?.syncUuid ?: "", labelBundleUuids, md5, job.jobId)
            },
            onDismiss = { claudeCancelTarget = null }
        )
    }

    if (showCameraPermissionDeniedDialog) {
        AlertDialog(
            onDismissRequest = { showCameraPermissionDeniedDialog = false },
            title = { Text("カメラ権限が必要です") },
            text = {
                Text("アプリ内カメラを使用するにはカメラへのアクセス許可が必要です。許可しない場合でも、右側の「標準カメラ」や下の「端末のギャラリーから写真を選択」から写真を追加できます。")
            },
            confirmButton = {
                Button(
                    onClick = {
                        showCameraPermissionDeniedDialog = false
                        val settingsIntent = Intent(
                            Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                            Uri.fromParts("package", context.packageName, null)
                        )
                        try {
                            context.startActivity(settingsIntent)
                        } catch (_: Exception) {
                        }
                    }
                ) {
                    Text("アプリ設定を開く")
                }
            },
            dismissButton = {
                TextButton(onClick = { showCameraPermissionDeniedDialog = false }) {
                    Text("閉じる")
                }
            }
        )
    }

    Box(modifier = Modifier.fillMaxSize()) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (itemId == 0) "新規出品登録" else "出品情報編集") },
                navigationIcon = {
                    IconButton(
                        enabled = !isSaving,
                        onClick = handleBackRequest,
                        modifier = Modifier.testTag("edit_back_button")
                    ) {
                        Icon(imageVector = Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "戻る")
                    }
                }
            )
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .padding(16.dp)
                .verticalScroll(rememberScrollState())
        ) {
            // フェーズ 6-2 (6-2-7): 承認待ちがある間は帯で知らせる (ダイアログは編集画面では画面連動で抑止される)。
            // タップすると抑止が解除され、アプリレベルの一括承認ダイアログが開く。
            if (pendingClaudeApprovals.isNotEmpty()) {
                Surface(
                    color = MaterialTheme.colorScheme.primaryContainer,
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable { viewModel.requestClaudeApprovalPeek() }
                ) {
                    Text(
                        text = "Muse承認待ち ${pendingClaudeApprovals.size} 件（タップで確認）",
                        fontSize = 13.sp,
                        color = MaterialTheme.colorScheme.onPrimaryContainer,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)
                    )
                }
                Spacer(modifier = Modifier.height(12.dp))
            }

            // ① 写真セクション（動線改善：撮影ボタンをメイン・大ボタンに配置）
            Text(
                text = "📸 商品写真 (${existingFilePaths.size + newImageUris.size}枚)",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold
            )
            Spacer(modifier = Modifier.height(8.dp))

            // メインの撮影ボタン（高さ 52.dp を維持したまま横並び 2 分割）
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Button(
                    onClick = {
                        val granted = ContextCompat.checkSelfPermission(
                            context,
                            Manifest.permission.CAMERA
                        ) == PackageManager.PERMISSION_GRANTED
                        if (granted) {
                            showCameraXDialog = true
                        } else {
                            cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
                        }
                    },
                    contentPadding = PaddingValues(horizontal = 8.dp),
                    modifier = Modifier
                        .weight(1f)
                        .height(52.dp)
                        .semantics(mergeDescendants = true) {}
                        .testTag("take_photo_camerax_button"),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.primary
                    )
                ) {
                    Icon(
                        imageVector = Icons.Default.CameraAlt,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = "アプリ内カメラ",
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }

                FilledTonalButton(
                    onClick = {
                        val stillIntent = Intent(MediaStore.INTENT_ACTION_STILL_IMAGE_CAMERA)
                        val launchedStandard = try {
                            if (stillIntent.resolveActivity(context.packageManager) != null) {
                                context.startActivity(stillIntent)
                                Toast.makeText(
                                    context,
                                    "標準カメラを起動しました（プロモード可）。撮影後は下の「端末のギャラリーから写真を選択」で取り込めます",
                                    Toast.LENGTH_LONG
                                ).show()
                                true
                            } else {
                                false
                            }
                        } catch (_: ActivityNotFoundException) {
                            false
                        } catch (_: Exception) {
                            false
                        }
                        if (!launchedStandard) {
                            val manager = PhotoStorageManager(context)
                            val pair = manager.createTempCameraPhotoUri(title.ifBlank { "Unassigned" })
                            if (pair != null) {
                                cameraPhotoUri = pair.first
                                tempCameraPath = pair.second
                                cameraLauncher.launch(pair.first)
                            } else {
                                Toast.makeText(
                                    context,
                                    "カメラ起動用のURI作成に失敗しました (FileProviderの設定をご確認ください)",
                                    Toast.LENGTH_LONG
                                ).show()
                            }
                        }
                    },
                    contentPadding = PaddingValues(horizontal = 8.dp),
                    modifier = Modifier
                        .weight(1f)
                        .height(52.dp)
                        .semantics(mergeDescendants = true) {}
                        .testTag("launch_standard_camera_button")
                ) {
                    Icon(
                        imageVector = Icons.Default.AddAPhoto,
                        contentDescription = null,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(
                        text = "標準カメラ",
                        fontSize = 14.sp,
                        fontWeight = FontWeight.SemiBold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }

            Spacer(modifier = Modifier.height(8.dp))

            // サブアクション：ギャラリーから選択
            OutlinedButton(
                onClick = {
                    pickerLauncher.launch(
                        PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)
                    )
                },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(44.dp)
                    .testTag("pick_images_button")
            ) {
                Icon(imageVector = Icons.Default.PhotoLibrary, contentDescription = null)
                Spacer(modifier = Modifier.width(6.dp))
                Text("端末のギャラリーから写真を選択", fontSize = 13.sp)
            }

            Spacer(modifier = Modifier.height(12.dp))

            // フェーズ 5-3: 複数選択モードのアクションバー
            if (photoSelectionMode) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(bottom = 8.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        "${selectedPhotoCount}枚選択中",
                        fontWeight = FontWeight.Bold,
                        fontSize = 13.sp
                    )
                    Spacer(modifier = Modifier.weight(1f))
                    Button(
                        onClick = { showBulkDeleteConfirmDialog = true },
                        enabled = selectedPhotoCount > 0,
                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
                    ) {
                        Text("削除", fontSize = 12.sp)
                    }
                    Button(
                        onClick = { tryOpenClaudeSendDialog() },
                        enabled = selectedPhotoCount > 0 && !isSendingToClaude
                    ) {
                        Text("Museに送る", fontSize = 12.sp)
                    }
                    OutlinedButton(onClick = { exitPhotoSelectionMode() }) {
                        Text("解除", fontSize = 12.sp)
                    }
                }
            }

            // 選択された写真のサムネイル一覧
            if (existingFilePaths.isNotEmpty() || newImageUris.isNotEmpty()) {
                LazyRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    itemsIndexed(existingFilePaths) { index, path ->
                        val photoEntity = loadedPhotosByPath[path]
                        val displayModel = photoEntity?.displayFile ?: File(path)
                        val photoKey = "path:$path"
                        val isPhotoSelected = selectedPhotoKeys.contains(photoKey)
                        Box(
                            modifier = Modifier
                                .size(90.dp)
                                .clip(RoundedCornerShape(8.dp))
                                .then(
                                    if (isPhotoSelected) Modifier.border(
                                        2.dp,
                                        MaterialTheme.colorScheme.primary,
                                        RoundedCornerShape(8.dp)
                                    ) else Modifier
                                )
                                .combinedClickable(
                                    onClick = {
                                        if (photoSelectionMode) {
                                            togglePhotoSelection(photoKey)
                                        } else {
                                            previewStartIndex = index
                                        }
                                    },
                                    onLongClick = {
                                        if (!photoSelectionMode) {
                                            photoSelectionMode = true
                                            if (!selectedPhotoKeys.contains(photoKey)) {
                                                selectedPhotoKeys.add(photoKey)
                                            }
                                        } else {
                                            // フェーズ 6-9 (Build 14): 選択モード中は長押しでプレビューを開く (§41.2 / 55-3)
                                            previewStartIndex = index
                                        }
                                    }
                                )
                                .testTag("edit_existing_photo_$index")
                        ) {
                            AsyncImage(
                                model = remember(displayModel) {
                                    ImageRequest.Builder(context)
                                        .data(displayModel)
                                        .size(360)
                                        .precision(Precision.INEXACT)
                                        .build()
                                },
                                contentDescription = "商品写真 ${index + 1}",
                                contentScale = ContentScale.Crop,
                                modifier = Modifier.fillMaxSize()
                            )
                            // フェーズ 6-5: 加工ラベルチップ (左下)
                            val claudeLabelKey = photoEntity?.md5Hash?.takeIf { it.isNotBlank() }?.let { md5 ->
                                viewModel.claudePhotoLabelKey(
                                    loadedItem?.syncUuid ?: "",
                                    labelBundleUuids,
                                    md5,
                                    claudeJobsForLabels,
                                    claudeLabelSeenAt,
                                    labelVisitSnapshot
                                )
                            }
                            if (claudeLabelKey != null) {
                                Box(
                                    modifier = Modifier
                                        .align(Alignment.BottomStart)
                                        .padding(3.dp)
                                        .background(claudeLabelColor(claudeLabelKey), RoundedCornerShape(4.dp))
                                        .padding(horizontal = 4.dp, vertical = 1.dp)
                                ) {
                                    Text(
                                        text = claudeLabelText(claudeLabelKey),
                                        color = if (claudeLabelKey == "approval") Color.Black else Color.White,
                                        fontSize = 8.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                }
                            }
                            // フェーズ 6-10 (§37.4 / §40.3): アクティブなラベルのチップに余白つきタップ領域を重ね、源泉ジョブの取消ダイアログを開く。
                            // 選択モード中はサムネイル全体タップが選択トグル優先のため、チップタップは置かない
                            if (!photoSelectionMode && (claudeLabelKey == "processing" || claudeLabelKey == "approval" || claudeLabelKey == "startwait" || claudeLabelKey == "waiting")) {
                                val claudeSourceJob = photoEntity?.md5Hash?.takeIf { it.isNotBlank() }?.let { md5 ->
                                    viewModel.claudePhotoLabelSourceJob(loadedItem?.syncUuid ?: "", labelBundleUuids, md5, claudeJobsForLabels)
                                }
                                if (claudeSourceJob != null) {
                                    Box(
                                        modifier = Modifier
                                            .align(Alignment.BottomStart)
                                            .size(width = 56.dp, height = 32.dp)
                                            .clickable { claudeCancelTarget = claudeSourceJob to (photoEntity?.md5Hash ?: "") }
                                    )
                                }
                            }
                            // フェーズ 6-14 (§48.5-4 任意): Muse 送信順番号の表示 (送信対象の写真のみ、§46 の番号スタンプと一致)
                            val sendOrderNumber = if (photoSelectionMode && isPhotoSelected) {
                                val md5 = photoEntity?.md5Hash?.takeIf { it.isNotBlank() }
                                if (md5 != null && photoEntity?.isPcSynced == true) {
                                    claudeEligiblePhotos.indexOfFirst { it.first == md5 }.takeIf { it >= 0 }?.plus(1)
                                } else {
                                    null
                                }
                            } else {
                                null
                            }
                            if (sendOrderNumber != null) {
                                Box(
                                    modifier = Modifier
                                        .align(Alignment.TopStart)
                                        .padding(3.dp)
                                        .background(Color.Black.copy(alpha = 0.65f), RoundedCornerShape(4.dp))
                                        .padding(horizontal = 4.dp, vertical = 1.dp)
                                ) {
                                    Text(
                                        text = "#$sendOrderNumber",
                                        color = Color.White,
                                        fontSize = 8.sp,
                                        fontWeight = FontWeight.Bold
                                    )
                                }
                            }
                            if (photoSelectionMode) {
                                Box(
                                    modifier = Modifier
                                        .size(26.dp)
                                        .background(Color.Black.copy(alpha = 0.65f))
                                        .align(Alignment.TopEnd)
                                ) {
                                    Checkbox(
                                        checked = isPhotoSelected,
                                        onCheckedChange = { togglePhotoSelection(photoKey) },
                                        modifier = Modifier.fillMaxSize()
                                    )
                                }
                            } else {
                                Box(
                                    modifier = Modifier
                                        .size(26.dp)
                                        .background(Color.Black.copy(alpha = 0.65f))
                                        .clickable { existingFilePaths.remove(path) }
                                        .align(Alignment.TopEnd)
                                        .testTag("remove_existing_photo_$index")
                                ) {
                                    Icon(
                                        imageVector = Icons.Default.Close,
                                        contentDescription = "写真を削除",
                                        tint = Color.White,
                                        modifier = Modifier
                                            .fillMaxSize()
                                            .padding(2.dp)
                                    )
                                }
                            }
                        }
                    }

                    itemsIndexed(newImageUris) { index, uri ->
                        val photoKey = "uri:$uri"
                        val isPhotoSelected = selectedPhotoKeys.contains(photoKey)
                        Box(
                            modifier = Modifier
                                .size(90.dp)
                                .clip(RoundedCornerShape(8.dp))
                                .then(
                                    if (isPhotoSelected) Modifier.border(
                                        2.dp,
                                        MaterialTheme.colorScheme.primary,
                                        RoundedCornerShape(8.dp)
                                    ) else Modifier
                                )
                                .combinedClickable(
                                    onClick = {
                                        if (photoSelectionMode) {
                                            togglePhotoSelection(photoKey)
                                        } else {
                                            previewStartIndex = existingFilePaths.size + index
                                        }
                                    },
                                    onLongClick = {
                                        if (!photoSelectionMode) {
                                            photoSelectionMode = true
                                            if (!selectedPhotoKeys.contains(photoKey)) {
                                                selectedPhotoKeys.add(photoKey)
                                            }
                                        } else {
                                            // フェーズ 6-9 (Build 14): 選択モード中は長押しでプレビューを開く (§41.2 / 55-3)
                                            previewStartIndex = existingFilePaths.size + index
                                        }
                                    }
                                )
                                .testTag("edit_new_photo_$index")
                        ) {
                            AsyncImage(
                                model = remember(uri) {
                                    ImageRequest.Builder(context)
                                        .data(uri)
                                        .size(360)
                                        .precision(Precision.INEXACT)
                                        .build()
                                },
                                contentDescription = "追加写真 ${index + 1}",
                                contentScale = ContentScale.Crop,
                                modifier = Modifier.fillMaxSize()
                            )
                            if (photoSelectionMode) {
                                Box(
                                    modifier = Modifier
                                        .size(26.dp)
                                        .background(Color.Black.copy(alpha = 0.65f))
                                        .align(Alignment.TopEnd)
                                ) {
                                    Checkbox(
                                        checked = isPhotoSelected,
                                        onCheckedChange = { togglePhotoSelection(photoKey) },
                                        modifier = Modifier.fillMaxSize()
                                    )
                                }
                            } else {
                                Box(
                                    modifier = Modifier
                                        .size(26.dp)
                                        .background(Color.Black.copy(alpha = 0.65f))
                                        .clickable { newImageUris.remove(uri) }
                                        .align(Alignment.TopEnd)
                                        .testTag("remove_new_photo_$index")
                                ) {
                                    Icon(
                                        imageVector = Icons.Default.Close,
                                        contentDescription = "写真を削除",
                                        tint = Color.White,
                                        modifier = Modifier
                                            .fillMaxSize()
                                            .padding(2.dp)
                                    )
                                }
                            }
                        }
                    }
                }
            } else {
                Text(
                    text = "※ 写真を追加すると、PC送信時（無圧縮ZIP）にまとめて転送できます",
                    color = Color.Gray,
                    fontSize = 12.sp
                )
            }

            Spacer(modifier = Modifier.height(20.dp))

            // ② 商品基本情報
            Text(
                text = "📝 商品基本情報",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold
            )
            Spacer(modifier = Modifier.height(8.dp))

            OutlinedTextField(
                value = titleFieldValue,
                onValueChange = { titleFieldValue = it },
                label = { Text("商品名・出品フォルダ名 (必須)") },
                singleLine = true,
                modifier = Modifier
                    .fillMaxWidth()
                    .testTag("item_title_input")
            )

            if (defaultSettings.titleTemplates.isNotEmpty()) {
                Spacer(modifier = Modifier.height(6.dp))
                TemplateInsertDropdown(
                    placeholder = "タイトル定型文を選択して挿入 (${defaultSettings.titleTemplates.size}件)",
                    templates = defaultSettings.titleTemplates,
                    dropdownTag = "title_template_dropdown",
                    itemTagPrefix = "insert_title_template_",
                    onTemplateSelected = { tpl ->
                        val expanded = expandTemplatePlaceholders(tpl.content, title, priceText)
                        titleFieldValue = insertTemplateAtCursor(titleFieldValue, expanded)
                    }
                )
            }

            Spacer(modifier = Modifier.height(10.dp))

            // 商品説明文（任意入力・PC送信ZIPに同梱）
            OutlinedTextField(
                value = descriptionFieldValue,
                onValueChange = { descriptionFieldValue = it },
                label = { Text("商品説明文 (任意・PC送信時にテキスト同梱)") },
                placeholder = { Text("商品の状態、サイズ、購入時期、注意事項など\n記述がある場合、PC送信時に『商品説明.txt』としてZIPに同梱されます") },
                minLines = 3,
                maxLines = 6,
                modifier = Modifier
                    .fillMaxWidth()
                    .testTag("item_description_input")
            )

            if (defaultSettings.descriptionTemplates.isNotEmpty()) {
                Spacer(modifier = Modifier.height(6.dp))
                TemplateInsertDropdown(
                    placeholder = "説明文定型文を選択して挿入 (${defaultSettings.descriptionTemplates.size}件)",
                    templates = defaultSettings.descriptionTemplates,
                    dropdownTag = "desc_template_dropdown",
                    itemTagPrefix = "insert_desc_template_",
                    onTemplateSelected = { tpl ->
                        val expanded = expandTemplatePlaceholders(tpl.content, title, priceText)
                        descriptionFieldValue = insertTemplateAtCursor(descriptionFieldValue, expanded)
                    }
                )
            }

            Spacer(modifier = Modifier.height(14.dp))

            // ステータス選択
            Text(text = "ステータス", fontWeight = FontWeight.Bold, fontSize = 14.sp)
            Spacer(modifier = Modifier.height(4.dp))
            Row(
                modifier = Modifier.horizontalScroll(rememberScrollState()),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                FilterChip(
                    selected = (status == "LISTED"),
                    onClick = { status = "LISTED" },
                    label = { Text("出品中") },
                    modifier = Modifier.testTag("status_listed_chip")
                )
                FilterChip(
                    selected = (status == "SOLD"),
                    onClick = { status = "SOLD" },
                    label = { Text("売却済み") },
                    modifier = Modifier.testTag("status_sold_chip")
                )
                FilterChip(
                    selected = (status == "SHIPPING"),
                    onClick = { status = "SHIPPING" },
                    label = { Text("発送準備中") },
                    modifier = Modifier.testTag("status_shipping_chip")
                )
                FilterChip(
                    selected = (status == "DRAFT"),
                    onClick = { status = "DRAFT" },
                    label = { Text("下書き中") },
                    modifier = Modifier.testTag("status_draft_chip")
                )
                FilterChip(
                    selected = (status == "DRAFT_DONE"),
                    onClick = { status = "DRAFT_DONE" },
                    label = { Text("下書き済み") },
                    modifier = Modifier.testTag("status_draft_done_chip")
                )
            }

            Spacer(modifier = Modifier.height(14.dp))

            // 価格入力
            OutlinedTextField(
                value = priceText,
                onValueChange = { priceText = it },
                label = { Text("出品価格 (円)") },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true,
                modifier = Modifier
                    .fillMaxWidth()
                    .testTag("item_price_input")
            )

            if (isSoldFamilyStatus(status)) {
                Spacer(modifier = Modifier.height(10.dp))
                OutlinedTextField(
                    value = soldPriceText,
                    onValueChange = { soldPriceText = it },
                    label = { Text("実際の売却価格 (値引き等があれば入力、空欄時は出品価格)") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("item_sold_price_input")
                )
            }

            Spacer(modifier = Modifier.height(16.dp))

            // ③ 費用設定アコーディオン
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)
                ),
                shape = RoundedCornerShape(12.dp)
            ) {
                Column(modifier = Modifier.padding(14.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text("個別費用設定 (カスタマイズ)", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                            Text(
                                if (useCustomSettings) "個別設定を適用中" else "デフォルト設定 (手数料${defaultSettings.defaultFeeRatePercent}% / 送料${defaultSettings.defaultShippingCost}円 / 経費${defaultSettings.defaultExpenseCost}円)",
                                fontSize = 11.sp,
                                color = Color.Gray
                            )
                        }
                        Switch(
                            checked = useCustomSettings,
                            onCheckedChange = { useCustomSettings = it },
                            modifier = Modifier.testTag("custom_settings_switch")
                        )
                    }

                    if (useCustomSettings) {
                        Spacer(modifier = Modifier.height(12.dp))

                        if (defaultSettings.presets.isNotEmpty()) {
                            Text(
                                text = "プリセットから一括反映",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                defaultSettings.presets.forEach { preset ->
                                    val matchesPreset = feeRatePercentText == preset.feeRatePercent.toString() &&
                                        shippingCostText == preset.shippingCost.toString() &&
                                        expenseCostText == preset.expenseCost.toString()
                                    FilterChip(
                                        selected = matchesPreset,
                                        onClick = {
                                            feeRatePercentText = preset.feeRatePercent.toString()
                                            shippingCostText = preset.shippingCost.toString()
                                            expenseCostText = preset.expenseCost.toString()
                                        },
                                        label = {
                                            Text(
                                                "${preset.name} (${preset.feeRatePercent}%/¥${preset.shippingCost})",
                                                fontSize = 11.sp
                                            )
                                        },
                                        modifier = Modifier.testTag("edit_apply_preset_${preset.id}")
                                    )
                                }
                            }
                            Spacer(modifier = Modifier.height(10.dp))
                        }

                        OutlinedTextField(
                            value = feeRatePercentText,
                            onValueChange = { feeRatePercentText = it },
                            label = { Text("販売手数料率 (%)") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            singleLine = true,
                            modifier = Modifier.fillMaxWidth()
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            FilterChip(selected = (feeRatePercentText == "5"), onClick = { feeRatePercentText = "5" }, label = { Text("5% (PayPay等)", fontSize = 11.sp) })
                            FilterChip(selected = (feeRatePercentText == "10"), onClick = { feeRatePercentText = "10" }, label = { Text("10% (メルカリ等)", fontSize = 11.sp) })
                        }

                        Spacer(modifier = Modifier.height(10.dp))
                        OutlinedTextField(
                            value = shippingCostText,
                            onValueChange = { shippingCostText = it },
                            label = { Text("送料 (円)") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            singleLine = true,
                            modifier = Modifier.fillMaxWidth()
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            FilterChip(selected = (shippingCostText == "210"), onClick = { shippingCostText = "210" }, label = { Text("210円 (ネコポス/ゆうパケ)", fontSize = 11.sp) })
                            FilterChip(selected = (shippingCostText == "450"), onClick = { shippingCostText = "450" }, label = { Text("450円 (コンパクト)", fontSize = 11.sp) })
                            FilterChip(selected = (shippingCostText == "750"), onClick = { shippingCostText = "750" }, label = { Text("750円 (60サイズ)", fontSize = 11.sp) })
                        }

                        Spacer(modifier = Modifier.height(10.dp))
                        OutlinedTextField(
                            value = expenseCostText,
                            onValueChange = { expenseCostText = it },
                            label = { Text("経費・梱包資材費 (円)") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            singleLine = true,
                            modifier = Modifier.fillMaxWidth()
                        )
                    }

                    Spacer(modifier = Modifier.height(14.dp))

                    // リアルタイム利益シミュレーション
                    Card(
                        modifier = Modifier.fillMaxWidth(),
                        colors = CardDefaults.cardColors(
                            containerColor = MaterialTheme.colorScheme.primaryContainer
                        ),
                        shape = RoundedCornerShape(8.dp)
                    ) {
                        Column(modifier = Modifier.padding(10.dp)) {
                            Text(
                                text = "🧮 利益シミュレーション",
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onPrimaryContainer
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = "売価 ${formatter.format(basePrice)} - 手数料(${effectiveFeePercent}%) ${formatter.format(calculatedFee)} - 送料 ${formatter.format(effectiveShipping)} - 経費 ${formatter.format(effectiveExpense)}",
                                fontSize = 11.sp,
                                color = MaterialTheme.colorScheme.onPrimaryContainer
                            )
                            Spacer(modifier = Modifier.height(2.dp))
                            Text(
                                text = "純利益: ${formatter.format(estimatedProfit)}",
                                fontSize = 16.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.primary
                            )
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            val editWarnings = evaluateListingWarnings(
                title = title,
                description = description,
                photoCount = existingFilePaths.size + newImageUris.size,
                price = priceText.toIntOrNull() ?: 0,
                estimatedProfit = estimatedProfit,
                status = status
            )
            if (editWarnings.isNotEmpty()) {
                ListingValidationBanner(warnings = editWarnings)
                Spacer(modifier = Modifier.height(14.dp))
            }

            Button(
                onClick = {
                    if (isSaving) return@Button
                    val price = priceText.toIntOrNull() ?: 0
                    val soldPrice = if (isSoldFamilyStatus(status)) (soldPriceText.toIntOrNull() ?: price) else 0
                    val shippingCost = if (useCustomSettings) {
                        shippingCostText.toIntOrNull() ?: defaultSettings.defaultShippingCost
                    } else {
                        defaultSettings.defaultShippingCost
                    }
                    val feeRatePercent = if (useCustomSettings) {
                        feeRatePercentText.toIntOrNull() ?: defaultSettings.defaultFeeRatePercent
                    } else {
                        defaultSettings.defaultFeeRatePercent
                    }
                    val expenseCost = if (useCustomSettings) {
                        expenseCostText.toIntOrNull() ?: defaultSettings.defaultExpenseCost
                    } else {
                        defaultSettings.defaultExpenseCost
                    }

                    if (title.isNotBlank()) {
                        isSaving = true
                        viewModel.saveItem(
                            id = itemId,
                            title = title,
                            description = description,
                            status = status,
                            price = price,
                            soldPrice = soldPrice,
                            shippingCost = shippingCost,
                            feeRatePercent = feeRatePercent,
                            expenseCost = expenseCost,
                            useCustomSettings = useCustomSettings,
                            imageUris = newImageUris.toList(),
                            existingFilePaths = existingFilePaths.toList(),
                            onSkippedInvalidPhotos = { skippedCount ->
                                Toast.makeText(
                                    context,
                                    "読み込めない空の写真 ${skippedCount}枚を除外して保存しました",
                                    Toast.LENGTH_LONG
                                ).show()
                            },
                            onError = { msg ->
                                isSaving = false
                                Toast.makeText(context, "保存エラー: $msg", Toast.LENGTH_LONG).show()
                            },
                            onSuccess = {
                                isSaving = false
                                onNavigateBack()
                            }
                        )
                    }
                },
                enabled = title.isNotBlank() && !isSaving,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp)
                    .testTag("save_item_button")
            ) {
                if (isSaving) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(18.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text("写真処理・保存中...", fontSize = 15.sp, fontWeight = FontWeight.Bold)
                } else {
                    Text("保存する", fontSize = 16.sp, fontWeight = FontWeight.Bold)
                }
            }
        }
    }

    if (showCameraXDialog) {
        CameraXCaptureDialog(
            itemTitle = title.ifBlank { "Unassigned" },
            onPhotosCaptured = { paths ->
                if (paths.isNotEmpty()) {
                    existingFilePaths.addAll(paths)
                    createdTempCameraPaths.addAll(paths)
                }
                showCameraXDialog = false
            },
            onDismiss = { showCameraXDialog = false }
        )
    }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TemplateInsertDropdown(
    placeholder: String,
    templates: List<com.example.myapplication.data.local.TextTemplate>,
    dropdownTag: String,
    itemTagPrefix: String,
    onTemplateSelected: (com.example.myapplication.data.local.TextTemplate) -> Unit
) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { expanded = !expanded },
        modifier = Modifier.fillMaxWidth()
    ) {
        OutlinedTextField(
            value = placeholder,
            onValueChange = {},
            readOnly = true,
            singleLine = true,
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
            textStyle = MaterialTheme.typography.bodySmall,
            modifier = Modifier
                .menuAnchor()
                .fillMaxWidth()
                .testTag(dropdownTag)
        )
        ExposedDropdownMenu(
            expanded = expanded,
            onDismissRequest = { expanded = false }
        ) {
            templates.forEach { tpl ->
                DropdownMenuItem(
                    text = {
                        Column {
                            Text(tpl.name, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                            Text(
                                text = tpl.content.replace("\n", " "),
                                fontSize = 11.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    },
                    onClick = {
                        expanded = false
                        onTemplateSelected(tpl)
                    },
                    modifier = Modifier.testTag("${itemTagPrefix}${tpl.id}")
                )
            }
        }
    }
}

private fun expandTemplatePlaceholders(templateText: String, title: String, priceText: String): String {
    val parsedPrice = priceText.toIntOrNull() ?: 0
    val formattedPrice = NumberFormat.getNumberInstance(Locale.JAPAN).format(parsedPrice)
    return templateText
        .replace("{title}", title.trim())
        .replace("{price}", formattedPrice)
}

private fun insertTemplateAtCursor(current: TextFieldValue, insertText: String): TextFieldValue {
    val start = current.selection.min.coerceIn(0, current.text.length)
    val end = current.selection.max.coerceIn(0, current.text.length)
    val updatedText = current.text.substring(0, start) + insertText + current.text.substring(end)
    val newCursor = start + insertText.length
    return TextFieldValue(text = updatedText, selection = TextRange(newCursor))
}

// フェーズ 6-5: 加工ラベルの表示名と色 (PC 管理画面のパレットに合わせる)
// フェーズ 6-10 (§37.1): 開始待ち (startwait) を追加
fun claudeLabelText(key: String): String = when (key) {
    "error" -> "エラー"
    "processing" -> "加工中"
    "approval" -> "承認待ち"
    "startwait" -> "開始待ち"
    "waiting" -> "待機中"
    "done" -> "加工済み"
    else -> ""
}

fun claudeLabelColor(key: String): Color = when (key) {
    "error" -> Color(0xFFE11D48)
    "processing" -> Color(0xFF2563EB)
    "approval" -> Color(0xFFFBBF24)
    "startwait" -> Color(0xFF0284C7)
    "waiting" -> Color(0xFF64748B)
    "done" -> Color(0xFF059669)
    else -> Color.Gray
}

// フェーズ 6-10 (§37.4): 取消ダイアログ等で使うジョブ状態の表示名 (ラベル導出と同じ語に揃える)
fun claudeJobStatusText(status: String): String = when (status) {
    "queued" -> "待機中"
    "approval_pending" -> "承認待ち"
    "approved" -> "開始待ち"
    "processing" -> "加工中"
    else -> status
}
