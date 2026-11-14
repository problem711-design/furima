package com.example.myapplication.ui.screens

import android.widget.Toast
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.CleaningServices
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.FileDownload
import androidx.compose.material.icons.filled.PhotoSizeSelectLarge
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.BuildConfig
import com.example.myapplication.data.PhotoStorageManager
import com.example.myapplication.data.ThumbnailCompressionEstimate
import com.example.myapplication.data.ThumbnailStorageStats
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.FeePreset
import com.example.myapplication.data.local.TextTemplate
import com.example.myapplication.ui.FleaMarketViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.Locale

private enum class TemplateCategory { TITLE, DESCRIPTION }

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SettingsStorageScreen(
    viewModel: FleaMarketViewModel
) {
    val context = LocalContext.current
    val currentSettings by viewModel.defaultSettings.collectAsState()
    val itemsWithPhotos by viewModel.itemsWithPhotos.collectAsState()
    val storageManager = remember(context) { PhotoStorageManager(context) }

    var activePresetId by remember(currentSettings) { mutableStateOf(currentSettings.activePresetId) }
    val presetsList = remember(currentSettings) {
        mutableStateListOf<FeePreset>().apply { addAll(currentSettings.presets) }
    }
    val titleTemplatesList = remember(currentSettings) {
        mutableStateListOf<TextTemplate>().apply { addAll(currentSettings.titleTemplates) }
    }
    val descriptionTemplatesList = remember(currentSettings) {
        mutableStateListOf<TextTemplate>().apply { addAll(currentSettings.descriptionTemplates) }
    }

    var feeRateText by remember(currentSettings) { mutableStateOf(currentSettings.defaultFeeRatePercent.toString()) }
    var shippingCostText by remember(currentSettings) { mutableStateOf(currentSettings.defaultShippingCost.toString()) }
    var expenseCostText by remember(currentSettings) { mutableStateOf(currentSettings.defaultExpenseCost.toString()) }

    var thumbQuality by remember(currentSettings) { mutableFloatStateOf(currentSettings.thumbnailQuality.toFloat()) }
    var thumbDim by remember(currentSettings) { mutableIntStateOf(currentSettings.thumbnailMaxDimension) }
    var cleanupDays by remember(currentSettings) { mutableIntStateOf(currentSettings.autoCleanupSoldDays) }

    var storageStats by remember { mutableStateOf(ThumbnailStorageStats()) }
    var compressionEstimate by remember { mutableStateOf(ThumbnailCompressionEstimate()) }

    var showAddPresetDialog by remember { mutableStateOf(false) }
    var presetToEdit by remember { mutableStateOf<FeePreset?>(null) }
    var presetToDelete by remember { mutableStateOf<FeePreset?>(null) }

    var editingTemplateCategory by remember { mutableStateOf<TemplateCategory?>(null) }
    var templateToEdit by remember { mutableStateOf<TextTemplate?>(null) }
    var templateToDelete by remember { mutableStateOf<Pair<TemplateCategory, TextTemplate>?>(null) }

    fun persistSettingsNow(
        targetPresets: List<FeePreset> = presetsList.toList(),
        targetActiveId: String = activePresetId,
        targetFee: Int = feeRateText.toIntOrNull() ?: currentSettings.defaultFeeRatePercent,
        targetShipping: Int = shippingCostText.toIntOrNull() ?: currentSettings.defaultShippingCost,
        targetExpense: Int = expenseCostText.toIntOrNull() ?: currentSettings.defaultExpenseCost,
        targetTitleTemplates: List<TextTemplate> = titleTemplatesList.toList(),
        targetDescTemplates: List<TextTemplate> = descriptionTemplatesList.toList()
    ) {
        val safeFee = targetFee.coerceAtLeast(0)
        val safeShip = targetShipping.coerceAtLeast(0)
        val safeExp = targetExpense.coerceAtLeast(0)
        val syncedPresets = targetPresets.map { p ->
            if (p.id == targetActiveId) {
                p.copy(feeRatePercent = safeFee, shippingCost = safeShip, expenseCost = safeExp)
            } else {
                p
            }
        }.ifEmpty { DefaultSettings.defaultPresets }

        viewModel.updateDefaultSettings(
            DefaultSettings(
                defaultFeeRatePercent = safeFee,
                defaultShippingCost = safeShip,
                defaultExpenseCost = safeExp,
                thumbnailQuality = thumbQuality.toInt(),
                thumbnailMaxDimension = thumbDim,
                autoCleanupSoldDays = cleanupDays,
                activePresetId = targetActiveId,
                presets = syncedPresets,
                titleTemplates = targetTitleTemplates,
                descriptionTemplates = targetDescTemplates
            )
        )
    }

    // アプリバージョン情報の取得 (BuildConfig優先)
    val versionName = remember { BuildConfig.VERSION_NAME }
    val versionCode = remember { BuildConfig.VERSION_CODE }

    LaunchedEffect(itemsWithPhotos) {
        val allPhotos = itemsWithPhotos.flatMap { it.photos }
        storageStats = withContext(Dispatchers.IO) {
            storageManager.measureLocalStorageStats(allPhotos)
        }
    }

    LaunchedEffect(thumbQuality, thumbDim, itemsWithPhotos) {
        val candidatePaths = itemsWithPhotos.flatMap { it.photos }
            .filter { !it.isOriginalDeleted && it.filePath.isNotBlank() }
            .map { it.filePath }
        compressionEstimate = withContext(Dispatchers.IO) {
            storageManager.estimateCompressedThumbnail(
                maxDim = thumbDim,
                quality = thumbQuality.toInt(),
                candidatePaths = candidatePaths
            )
        }
    }

    // プリセット追加ダイアログ
    if (showAddPresetDialog) {
        var newName by remember { mutableStateOf("") }
        var newFeeText by remember { mutableStateOf("10") }
        var newShippingText by remember { mutableStateOf("210") }
        var newExpenseText by remember { mutableStateOf("55") }

        AlertDialog(
            onDismissRequest = { showAddPresetDialog = false },
            title = { Text("新しい費用プリセットを追加") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(
                        value = newName,
                        onValueChange = { newName = it },
                        label = { Text("プリセット名 (例: メルカリ大型)") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_add_name_input")
                    )
                    OutlinedTextField(
                        value = newFeeText,
                        onValueChange = { newFeeText = it },
                        label = { Text("販売手数料率 (%)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_add_fee_input")
                    )
                    OutlinedTextField(
                        value = newShippingText,
                        onValueChange = { newShippingText = it },
                        label = { Text("送料 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_add_shipping_input")
                    )
                    OutlinedTextField(
                        value = newExpenseText,
                        onValueChange = { newExpenseText = it },
                        label = { Text("梱包資材費・経費 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_add_expense_input")
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        if (newName.isNotBlank()) {
                            val newPreset = FeePreset(
                                id = "custom_${System.currentTimeMillis()}",
                                name = newName.trim(),
                                feeRatePercent = (newFeeText.toIntOrNull() ?: 10).coerceAtLeast(0),
                                shippingCost = (newShippingText.toIntOrNull() ?: 210).coerceAtLeast(0),
                                expenseCost = (newExpenseText.toIntOrNull() ?: 55).coerceAtLeast(0)
                            )
                            presetsList.add(newPreset)
                            activePresetId = newPreset.id
                            feeRateText = newPreset.feeRatePercent.toString()
                            shippingCostText = newPreset.shippingCost.toString()
                            expenseCostText = newPreset.expenseCost.toString()
                            showAddPresetDialog = false
                            persistSettingsNow(
                                targetPresets = presetsList.toList(),
                                targetActiveId = newPreset.id,
                                targetFee = newPreset.feeRatePercent,
                                targetShipping = newPreset.shippingCost,
                                targetExpense = newPreset.expenseCost
                            )
                            Toast.makeText(context, "「${newPreset.name}」を追加しデフォルトに設定しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    enabled = newName.isNotBlank(),
                    modifier = Modifier.testTag("confirm_add_preset_button")
                ) {
                    Text("追加してデフォルトにする")
                }
            },
            dismissButton = {
                TextButton(onClick = { showAddPresetDialog = false }) {
                    Text("キャンセル")
                }
            }
        )
    }

    // プリセット編集ダイアログ
    presetToEdit?.let { target ->
        var editName by remember(target.id) { mutableStateOf(target.name) }
        var editFeeText by remember(target.id) { mutableStateOf(target.feeRatePercent.toString()) }
        var editShippingText by remember(target.id) { mutableStateOf(target.shippingCost.toString()) }
        var editExpenseText by remember(target.id) { mutableStateOf(target.expenseCost.toString()) }

        AlertDialog(
            onDismissRequest = { presetToEdit = null },
            title = { Text("プリセットを編集") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(
                        value = editName,
                        onValueChange = { editName = it },
                        label = { Text("プリセット名") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_edit_name_input")
                    )
                    OutlinedTextField(
                        value = editFeeText,
                        onValueChange = { editFeeText = it },
                        label = { Text("販売手数料率 (%)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_edit_fee_input")
                    )
                    OutlinedTextField(
                        value = editShippingText,
                        onValueChange = { editShippingText = it },
                        label = { Text("送料 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_edit_shipping_input")
                    )
                    OutlinedTextField(
                        value = editExpenseText,
                        onValueChange = { editExpenseText = it },
                        label = { Text("梱包資材費・経費 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("preset_edit_expense_input")
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        if (editName.isNotBlank()) {
                            val updatedPreset = target.copy(
                                name = editName.trim(),
                                feeRatePercent = (editFeeText.toIntOrNull() ?: target.feeRatePercent).coerceAtLeast(0),
                                shippingCost = (editShippingText.toIntOrNull() ?: target.shippingCost).coerceAtLeast(0),
                                expenseCost = (editExpenseText.toIntOrNull() ?: target.expenseCost).coerceAtLeast(0)
                            )
                            val idx = presetsList.indexOfFirst { it.id == target.id }
                            if (idx >= 0) {
                                presetsList[idx] = updatedPreset
                            }
                            if (activePresetId == target.id) {
                                feeRateText = updatedPreset.feeRatePercent.toString()
                                shippingCostText = updatedPreset.shippingCost.toString()
                                expenseCostText = updatedPreset.expenseCost.toString()
                            }
                            presetToEdit = null
                            persistSettingsNow(targetPresets = presetsList.toList())
                            Toast.makeText(context, "プリセットを更新しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    enabled = editName.isNotBlank(),
                    modifier = Modifier.testTag("confirm_edit_preset_button")
                ) {
                    Text("保存")
                }
            },
            dismissButton = {
                TextButton(onClick = { presetToEdit = null }) {
                    Text("キャンセル")
                }
            }
        )
    }

    // プリセット削除ダイアログ
    presetToDelete?.let { preset ->
        AlertDialog(
            onDismissRequest = { presetToDelete = null },
            icon = { Icon(Icons.Default.Delete, contentDescription = null, tint = Color.Red) },
            title = { Text("プリセットの削除") },
            text = { Text("「${preset.name}」プリセットを削除しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        if (presetsList.size > 1) {
                            presetsList.removeAll { it.id == preset.id }
                            if (activePresetId == preset.id) {
                                val fallback = presetsList.first()
                                activePresetId = fallback.id
                                feeRateText = fallback.feeRatePercent.toString()
                                shippingCostText = fallback.shippingCost.toString()
                                expenseCostText = fallback.expenseCost.toString()
                            }
                            persistSettingsNow(targetPresets = presetsList.toList())
                            Toast.makeText(context, "プリセットを削除しました", Toast.LENGTH_SHORT).show()
                        }
                        presetToDelete = null
                    },
                    modifier = Modifier.testTag("confirm_delete_preset_button")
                ) {
                    Text("削除する")
                }
            },
            dismissButton = {
                TextButton(onClick = { presetToDelete = null }) { Text("キャンセル") }
            }
        )
    }

    // テンプレート追加・編集ダイアログ
    val activeTplCategory = editingTemplateCategory
    if (activeTplCategory != null) {
        val editing = templateToEdit
        var tplName by remember(activeTplCategory, editing?.id) { mutableStateOf(editing?.name ?: "") }
        var tplContent by remember(activeTplCategory, editing?.id) { mutableStateOf(editing?.content ?: "") }
        val isTitleCategory = (activeTplCategory == TemplateCategory.TITLE)

        AlertDialog(
            onDismissRequest = {
                editingTemplateCategory = null
                templateToEdit = null
            },
            title = {
                Text(
                    when {
                        editing != null && isTitleCategory -> "タイトルテンプレートを編集"
                        editing != null -> "商品説明テンプレートを編集"
                        isTitleCategory -> "タイトルテンプレートを追加"
                        else -> "商品説明テンプレートを追加"
                    }
                )
            },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedTextField(
                        value = tplName,
                        onValueChange = { tplName = it },
                        label = { Text("表示名 (例: 美品タグ / 発送定型文)") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().testTag("template_name_input")
                    )
                    OutlinedTextField(
                        value = tplContent,
                        onValueChange = { tplContent = it },
                        label = { Text(if (isTitleCategory) "挿入するタイトル文字列" else "挿入する説明文本文") },
                        singleLine = isTitleCategory,
                        minLines = if (isTitleCategory) 1 else 3,
                        maxLines = if (isTitleCategory) 2 else 8,
                        modifier = Modifier.fillMaxWidth().testTag("template_content_input")
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        if (tplName.isNotBlank() && tplContent.isNotEmpty()) {
                            val targetList = if (isTitleCategory) titleTemplatesList else descriptionTemplatesList
                            if (editing != null) {
                                val updated = editing.copy(name = tplName.trim(), content = tplContent)
                                val idx = targetList.indexOfFirst { it.id == editing.id }
                                if (idx >= 0) targetList[idx] = updated
                            } else {
                                targetList.add(
                                    TextTemplate(
                                        id = "tpl_${System.currentTimeMillis()}",
                                        name = tplName.trim(),
                                        content = tplContent
                                    )
                                )
                            }
                            editingTemplateCategory = null
                            templateToEdit = null
                            persistSettingsNow(
                                targetTitleTemplates = titleTemplatesList.toList(),
                                targetDescTemplates = descriptionTemplatesList.toList()
                            )
                            Toast.makeText(context, "テンプレートを保存・同期しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    enabled = tplName.isNotBlank() && tplContent.isNotEmpty(),
                    modifier = Modifier.testTag("confirm_save_template_button")
                ) {
                    Text("保存")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = {
                        editingTemplateCategory = null
                        templateToEdit = null
                    }
                ) {
                    Text("キャンセル")
                }
            }
        )
    }

    // テンプレート削除ダイアログ
    templateToDelete?.let { (category, tpl) ->
        AlertDialog(
            onDismissRequest = { templateToDelete = null },
            icon = { Icon(Icons.Default.Delete, contentDescription = null, tint = Color.Red) },
            title = { Text("テンプレートの削除") },
            text = { Text("「${tpl.name}」を削除しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        if (category == TemplateCategory.TITLE) {
                            titleTemplatesList.removeAll { it.id == tpl.id }
                        } else {
                            descriptionTemplatesList.removeAll { it.id == tpl.id }
                        }
                        templateToDelete = null
                        persistSettingsNow(
                            targetTitleTemplates = titleTemplatesList.toList(),
                            targetDescTemplates = descriptionTemplatesList.toList()
                        )
                        Toast.makeText(context, "テンプレートを削除しました", Toast.LENGTH_SHORT).show()
                    },
                    modifier = Modifier.testTag("confirm_delete_template_button")
                ) {
                    Text("削除する")
                }
            },
            dismissButton = {
                TextButton(onClick = { templateToDelete = null }) { Text("キャンセル") }
            }
        )
    }

    Scaffold(
        topBar = {
            Surface(
                color = MaterialTheme.colorScheme.primary,
                shadowElevation = 2.dp
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 14.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "設定 & ストレージ管理",
                        fontWeight = FontWeight.Bold,
                        fontSize = 17.sp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                }
            }
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .padding(14.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // 1. 基本費用設定 ＆ デフォルトプリセット管理
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.weight(1f)) {
                            Icon(imageVector = Icons.Default.Settings, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                            Spacer(modifier = Modifier.width(8.dp))
                            Text("基本費用設定 & プリセット", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                        }

                        OutlinedButton(
                            onClick = { showAddPresetDialog = true },
                            modifier = Modifier.testTag("add_preset_button")
                        ) {
                            Icon(Icons.Default.Add, contentDescription = null, modifier = Modifier.size(16.dp))
                            Spacer(modifier = Modifier.width(4.dp))
                            Text("追加", fontSize = 12.sp)
                        }
                    }

                    Text(
                        text = "新規出品時に標準適用されるデフォルトプリセットの選択・値編集・削除が行えます（変更すると即座にPCへ同期されます）。",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        modifier = Modifier.padding(top = 4.dp, bottom = 10.dp)
                    )

                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        presetsList.forEach { preset ->
                            val isSelected = (activePresetId == preset.id)
                            Surface(
                                modifier = Modifier.fillMaxWidth(),
                                shape = RoundedCornerShape(10.dp),
                                color = if (isSelected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
                                border = BorderStroke(
                                    width = if (isSelected) 1.5.dp else 1.dp,
                                    color = if (isSelected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outlineVariant
                                )
                            ) {
                                Row(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(horizontal = 10.dp, vertical = 6.dp),
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Column(modifier = Modifier.weight(1f)) {
                                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                            if (isSelected) {
                                                Icon(
                                                    imageVector = Icons.Default.Star,
                                                    contentDescription = "現在のデフォルト",
                                                    tint = MaterialTheme.colorScheme.primary,
                                                    modifier = Modifier.size(15.dp)
                                                )
                                            }
                                            Text(
                                                text = preset.name,
                                                fontSize = 13.sp,
                                                fontWeight = if (isSelected) FontWeight.Bold else FontWeight.SemiBold,
                                                maxLines = 1,
                                                overflow = TextOverflow.Ellipsis
                                            )
                                        }
                                        Text(
                                            text = "手数料 ${preset.feeRatePercent}% / 送料 ¥${preset.shippingCost} / 経費 ¥${preset.expenseCost}",
                                            fontSize = 11.sp,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant
                                        )
                                    }

                                    if (!isSelected) {
                                        TextButton(
                                            onClick = {
                                                activePresetId = preset.id
                                                feeRateText = preset.feeRatePercent.toString()
                                                shippingCostText = preset.shippingCost.toString()
                                                expenseCostText = preset.expenseCost.toString()
                                                persistSettingsNow(
                                                    targetActiveId = preset.id,
                                                    targetFee = preset.feeRatePercent,
                                                    targetShipping = preset.shippingCost,
                                                    targetExpense = preset.expenseCost
                                                )
                                                Toast.makeText(context, "「${preset.name}」をデフォルトに変更しました", Toast.LENGTH_SHORT).show()
                                            },
                                            modifier = Modifier.testTag("set_default_preset_${preset.id}")
                                        ) {
                                            Text("標準にする", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                                        }
                                    } else {
                                        Surface(
                                            color = MaterialTheme.colorScheme.primary,
                                            shape = RoundedCornerShape(6.dp)
                                        ) {
                                            Text(
                                                text = "デフォルト",
                                                color = MaterialTheme.colorScheme.onPrimary,
                                                fontSize = 10.sp,
                                                fontWeight = FontWeight.Bold,
                                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 3.dp)
                                            )
                                        }
                                    }

                                    IconButton(
                                        onClick = { presetToEdit = preset },
                                        modifier = Modifier.testTag("edit_preset_${preset.id}")
                                    ) {
                                        Icon(
                                            imageVector = Icons.Default.Edit,
                                            contentDescription = "プリセットを編集",
                                            modifier = Modifier.size(18.dp)
                                        )
                                    }

                                    if (presetsList.size > 1) {
                                        IconButton(
                                            onClick = { presetToDelete = preset },
                                            modifier = Modifier.testTag("delete_preset_${preset.id}")
                                        ) {
                                            Icon(
                                                imageVector = Icons.Default.Delete,
                                                contentDescription = "プリセットを削除",
                                                tint = MaterialTheme.colorScheme.error,
                                                modifier = Modifier.size(18.dp)
                                            )
                                        }
                                    }
                                }
                            }
                        }
                    }

                    Spacer(modifier = Modifier.height(12.dp))

                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedTextField(
                            value = feeRateText,
                            onValueChange = { feeRateText = it },
                            label = { Text("手数料率 (%)") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            singleLine = true,
                            modifier = Modifier.weight(1f)
                        )
                        OutlinedTextField(
                            value = shippingCostText,
                            onValueChange = { shippingCostText = it },
                            label = { Text("送料 (円)") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            singleLine = true,
                            modifier = Modifier.weight(1f)
                        )
                        OutlinedTextField(
                            value = expenseCostText,
                            onValueChange = { expenseCostText = it },
                            label = { Text("梱包資材費 (円)") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            singleLine = true,
                            modifier = Modifier.weight(1f)
                        )
                    }
                }
            }

            // 2. タイトル・商品説明テンプレート管理
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.Description, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("タイトル・商品説明テンプレート", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                    Text(
                        text = "出品編集画面やまとめ買い作成画面でカーソル位置へワンタップ挿入できる定型文を管理します（PC管理画面とも自動同期）。",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f)
                    )

                    // タイトル用テンプレート
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("タイトル用テンプレート (${titleTemplatesList.size}件)", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                        OutlinedButton(
                            onClick = {
                                templateToEdit = null
                                editingTemplateCategory = TemplateCategory.TITLE
                            },
                            modifier = Modifier.testTag("add_title_template_button")
                        ) {
                            Icon(Icons.Default.Add, contentDescription = null, modifier = Modifier.size(16.dp))
                            Spacer(modifier = Modifier.width(4.dp))
                            Text("追加", fontSize = 12.sp)
                        }
                    }

                    if (titleTemplatesList.isEmpty()) {
                        Text(
                            text = "※ 未登録です（例：【美品】【即購入OK】など）",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f)
                        )
                    } else {
                        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            titleTemplatesList.forEach { tpl ->
                                TemplateRowItem(
                                    template = tpl,
                                    onEdit = {
                                        templateToEdit = tpl
                                        editingTemplateCategory = TemplateCategory.TITLE
                                    },
                                    onDelete = {
                                        templateToDelete = Pair(TemplateCategory.TITLE, tpl)
                                    }
                                )
                            }
                        }
                    }

                    // 商品説明用テンプレート
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("商品説明用テンプレート (${descriptionTemplatesList.size}件)", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                        OutlinedButton(
                            onClick = {
                                templateToEdit = null
                                editingTemplateCategory = TemplateCategory.DESCRIPTION
                            },
                            modifier = Modifier.testTag("add_desc_template_button")
                        ) {
                            Icon(Icons.Default.Add, contentDescription = null, modifier = Modifier.size(16.dp))
                            Spacer(modifier = Modifier.width(4.dp))
                            Text("追加", fontSize = 12.sp)
                        }
                    }

                    if (descriptionTemplatesList.isEmpty()) {
                        Text(
                            text = "※ 未登録です（例：ペット・喫煙者なし / 防水梱包発送など）",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f)
                        )
                    } else {
                        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            descriptionTemplatesList.forEach { tpl ->
                                TemplateRowItem(
                                    template = tpl,
                                    onEdit = {
                                        templateToEdit = tpl
                                        editingTemplateCategory = TemplateCategory.DESCRIPTION
                                    },
                                    onDelete = {
                                        templateToDelete = Pair(TemplateCategory.DESCRIPTION, tpl)
                                    }
                                )
                            }
                        }
                    }
                }
            }

            // 3. 売上データのエクスポート
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.FileDownload, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("売上データのCSVエクスポート", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                    Text(
                        text = "出品商品・売却履歴・利益計算の全データをCSV形式で出力し、ExcelやGoogleスプレッドシート等で管理できます。",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        modifier = Modifier.padding(top = 4.dp, bottom = 12.dp)
                    )

                    Button(
                        onClick = { viewModel.exportAllToCsv(context) },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Icon(imageVector = Icons.Default.FileDownload, contentDescription = null, modifier = Modifier.size(16.dp))
                        Spacer(modifier = Modifier.width(6.dp))
                        Text("売上CSVファイルを出力・共有")
                    }
                }
            }

            // 4. 写真ストレージ管理（PC母艦同期連携）
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.CleaningServices, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("写真ストレージ管理方針", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                    Text(
                        text = "PCへ同期が完了した出品写真は、スマホ端末内では自動的に軽量サムネイルに切り替わり、スマホの空き容量を最大化します。高画質原本の長期保存や売却済み写真のデータクリーンアップは、PC母艦ブラウザ管理画面からワンクリックで安全に実行できます。",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        modifier = Modifier.padding(top = 4.dp)
                    )
                }
            }

            // 5. サムネイル圧縮率・画質設定
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.PhotoSizeSelectLarge, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("サムネイル保存画質設定", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                    Text(
                        text = "一覧表示用サムネイルの保存品質を調整できます。スマホの容量やカメラ性能に合わせて選択してください。",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        modifier = Modifier.padding(top = 4.dp, bottom = 8.dp)
                    )

                    Text("JPEG圧縮品質: ${thumbQuality.toInt()}%", fontWeight = FontWeight.SemiBold, fontSize = 12.sp)
                    Slider(
                        value = thumbQuality,
                        onValueChange = { thumbQuality = it },
                        valueRange = 40f..95f,
                        steps = 10,
                        modifier = Modifier.fillMaxWidth()
                    )
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        FilterChip(selected = (thumbQuality.toInt() == 50), onClick = { thumbQuality = 50f }, label = { Text("軽量 (50%)", fontSize = 11.sp) })
                        FilterChip(selected = (thumbQuality.toInt() == 75), onClick = { thumbQuality = 75f }, label = { Text("標準 (75%)", fontSize = 11.sp) })
                        FilterChip(selected = (thumbQuality.toInt() == 90), onClick = { thumbQuality = 90f }, label = { Text("高画質 (90%)", fontSize = 11.sp) })
                    }

                    Spacer(modifier = Modifier.height(10.dp))

                    Text("サムネイル最大解像度", fontWeight = FontWeight.SemiBold, fontSize = 12.sp)
                    Spacer(modifier = Modifier.height(4.dp))
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        FilterChip(selected = (thumbDim == 320), onClick = { thumbDim = 320 }, label = { Text("小 (320px)", fontSize = 11.sp) })
                        FilterChip(selected = (thumbDim == 480), onClick = { thumbDim = 480 }, label = { Text("中 (480px・推奨)", fontSize = 11.sp) })
                        FilterChip(selected = (thumbDim == 640), onClick = { thumbDim = 640 }, label = { Text("大 (640px)", fontSize = 11.sp) })
                    }

                    Spacer(modifier = Modifier.height(12.dp))

                    Surface(
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(10.dp),
                        color = MaterialTheme.colorScheme.surface
                    ) {
                        Column(
                            modifier = Modifier.padding(12.dp),
                            verticalArrangement = Arrangement.spacedBy(6.dp)
                        ) {
                            Text(
                                text = "容量シミュレーション & 端末内使用量",
                                fontWeight = FontWeight.Bold,
                                fontSize = 12.sp,
                                color = MaterialTheme.colorScheme.primary
                            )
                            Text(
                                text = "現在の端末内サムネイル: ${storageStats.thumbnailCount}枚 (${formatStorageBytes(storageStats.thumbnailTotalBytes)}) / 原本: ${storageStats.originalCount}枚 (${formatStorageBytes(storageStats.originalTotalBytes)})",
                                fontSize = 11.sp,
                                modifier = Modifier.testTag("thumbnail_storage_usage_text")
                            )
                            val estKb = String.format(Locale.JAPAN, "%.1f", compressionEstimate.estimatedBytes / 1024.0)
                            val baseKb = String.format(Locale.JAPAN, "%.1f", compressionEstimate.baselineBytes / 1024.0)
                            val sourceLabel = if (compressionEstimate.usedExistingPhoto) "端末内写真試算" else "標準テスト画像試算"
                            Text(
                                text = "現在設定での1枚あたり目安サイズ (${sourceLabel}): 約 ${estKb} KB（高画質 ${baseKb} KB 比 ${compressionEstimate.reductionPercent}%削減）",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.SemiBold,
                                color = MaterialTheme.colorScheme.secondary,
                                modifier = Modifier.testTag("thumbnail_estimated_size_text")
                            )
                            Text(
                                text = "※設定変更は「設定を保存する」後に新しく登録・撮影する写真から適用されます",
                                fontSize = 10.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.75f)
                            )
                        }
                    }
                }
            }

            // 設定保存ボタン
            Button(
                onClick = {
                    val fee = (feeRateText.toIntOrNull() ?: 5).coerceAtLeast(0)
                    val shipping = (shippingCostText.toIntOrNull() ?: 210).coerceAtLeast(0)
                    val expense = (expenseCostText.toIntOrNull() ?: 55).coerceAtLeast(0)

                    persistSettingsNow(
                        targetPresets = presetsList.toList(),
                        targetActiveId = activePresetId,
                        targetFee = fee,
                        targetShipping = shipping,
                        targetExpense = expense,
                        targetTitleTemplates = titleTemplatesList.toList(),
                        targetDescTemplates = descriptionTemplatesList.toList()
                    )
                    Toast.makeText(context, "設定を保存しました", Toast.LENGTH_SHORT).show()
                },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(48.dp)
                    .testTag("save_settings_button"),
                shape = RoundedCornerShape(12.dp)
            ) {
                Text("設定を保存する", fontWeight = FontWeight.Bold, fontSize = 14.sp)
            }

            // 最下部：Android Studio同期バージョン表記
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 12.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        text = "Remix フリマ出品・売上管理",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f)
                    )
                    Text(
                        text = "バージョン $versionName (Build $versionCode)",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.5f)
                    )
                }
            }
        }
    }
}

@Composable
private fun TemplateRowItem(
    template: TextTemplate,
    onEdit: () -> Unit,
    onDelete: () -> Unit
) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(8.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 10.dp, end = 4.dp, top = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = template.name,
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Text(
                    text = template.content.replace("\n", " "),
                    fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis
                )
            }
            IconButton(
                onClick = onEdit,
                modifier = Modifier.testTag("edit_template_${template.id}")
            ) {
                Icon(
                    imageVector = Icons.Default.Edit,
                    contentDescription = "テンプレートを編集",
                    modifier = Modifier.size(18.dp)
                )
            }
            IconButton(
                onClick = onDelete,
                modifier = Modifier.testTag("delete_template_${template.id}")
            ) {
                Icon(
                    imageVector = Icons.Default.Delete,
                    contentDescription = "テンプレートを削除",
                    tint = MaterialTheme.colorScheme.error,
                    modifier = Modifier.size(18.dp)
                )
            }
        }
    }
}

private fun formatStorageBytes(bytes: Long): String {
    return when {
        bytes >= 1024L * 1024L -> String.format(Locale.JAPAN, "%.2f MB", bytes / (1024.0 * 1024.0))
        bytes >= 1024L -> String.format(Locale.JAPAN, "%.1f KB", bytes / 1024.0)
        else -> "$bytes B"
    }
}
