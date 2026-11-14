package com.example.myapplication.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.BatchEditSpec
import com.example.myapplication.data.BatchPriceMode
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemWithPhotos
import java.text.NumberFormat
import java.util.Locale

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun BatchEditDialog(
    selectedItems: List<ItemWithPhotos>,
    defaultSettings: DefaultSettings,
    onDismiss: () -> Unit,
    onConfirm: (BatchEditSpec) -> Unit
) {
    var enableStatus by remember { mutableStateOf(false) }
    var selectedStatus by remember { mutableStateOf("LISTED") }

    var enableCostPreset by remember { mutableStateOf(false) }
    var useDefaultSettings by remember { mutableStateOf(true) }
    var selectedPresetId by remember { mutableStateOf(defaultSettings.activePresetId) }

    var enablePrice by remember { mutableStateOf(false) }
    var priceMode by remember { mutableStateOf(BatchPriceMode.DELTA_YEN) }
    var isNegativeDelta by remember { mutableStateOf(true) }
    var priceValueText by remember { mutableStateOf("100") }

    val formatter = remember { NumberFormat.getCurrencyInstance(Locale.JAPAN) }
    val selectedPreset = defaultSettings.presets.firstOrNull { it.id == selectedPresetId }
        ?: defaultSettings.presets.firstOrNull()

    val parsedAbsValue = priceValueText.toIntOrNull()?.coerceAtLeast(0) ?: 0
    val signedPriceValue = when (priceMode) {
        BatchPriceMode.NONE -> 0
        BatchPriceMode.FIXED -> parsedAbsValue
        BatchPriceMode.DELTA_YEN, BatchPriceMode.DELTA_PERCENT -> if (isNegativeDelta) -parsedAbsValue else parsedAbsValue
    }

    val effectiveSpec = BatchEditSpec(
        newStatus = if (enableStatus) selectedStatus else null,
        applyCostPreset = enableCostPreset,
        useCustomSettings = if (enableCostPreset) !useDefaultSettings else false,
        feeRatePercent = if (!useDefaultSettings && selectedPreset != null) selectedPreset.feeRatePercent else defaultSettings.defaultFeeRatePercent,
        shippingCost = if (!useDefaultSettings && selectedPreset != null) selectedPreset.shippingCost else defaultSettings.defaultShippingCost,
        expenseCost = if (!useDefaultSettings && selectedPreset != null) selectedPreset.expenseCost else defaultSettings.defaultExpenseCost,
        priceMode = if (enablePrice) priceMode else BatchPriceMode.NONE,
        priceValue = signedPriceValue
    )

    val hasAnyChange = enableStatus || enableCostPreset || enablePrice
    val sampleItem = selectedItems.firstOrNull()?.item

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = "一括編集 (${selectedItems.size}件)",
                fontWeight = FontWeight.Bold,
                fontSize = 18.sp
            )
        },
        text = {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                Text(
                    text = "ONにした項目だけを上書きします（OFFの項目は元の値を保持）。",
                    fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )

                // 1. ステータス変更
                Card(
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                    shape = RoundedCornerShape(10.dp)
                ) {
                    Column(modifier = Modifier.padding(10.dp)) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text("ステータスを変更", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                            Switch(
                                checked = enableStatus,
                                onCheckedChange = { enableStatus = it },
                                modifier = Modifier.testTag("batch_edit_status_switch")
                            )
                        }
                        if (enableStatus) {
                            Spacer(modifier = Modifier.height(6.dp))
                            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                listOf("LISTED" to "出品中", "DRAFT" to "下書き", "SOLD" to "売却済").forEach { (code, label) ->
                                    FilterChip(
                                        selected = selectedStatus == code,
                                        onClick = { selectedStatus = code },
                                        label = { Text(label, fontSize = 11.sp) }
                                    )
                                }
                            }
                        }
                    }
                }

                // 2. 送料・手数料プリセット適用
                Card(
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                    shape = RoundedCornerShape(10.dp)
                ) {
                    Column(modifier = Modifier.padding(10.dp)) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text("費用プリセットを適用", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                            Switch(
                                checked = enableCostPreset,
                                onCheckedChange = { enableCostPreset = it },
                                modifier = Modifier.testTag("batch_edit_preset_switch")
                            )
                        }
                        if (enableCostPreset) {
                            Spacer(modifier = Modifier.height(6.dp))
                            FlowRow(
                                horizontalArrangement = Arrangement.spacedBy(6.dp),
                                verticalArrangement = Arrangement.spacedBy(4.dp)
                            ) {
                                FilterChip(
                                    selected = useDefaultSettings,
                                    onClick = { useDefaultSettings = true },
                                    label = { Text("基本設定に従う", fontSize = 11.sp) }
                                )
                                defaultSettings.presets.forEach { preset ->
                                    FilterChip(
                                        selected = !useDefaultSettings && selectedPresetId == preset.id,
                                        onClick = {
                                            useDefaultSettings = false
                                            selectedPresetId = preset.id
                                        },
                                        label = {
                                            Text(
                                                "${preset.name} (${preset.feeRatePercent}%/¥${preset.shippingCost})",
                                                fontSize = 11.sp
                                            )
                                        }
                                    )
                                }
                            }
                        }
                    }
                }

                // 3. 販売価格調整 (0円未満クランプ)
                Card(
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                    shape = RoundedCornerShape(10.dp)
                ) {
                    Column(modifier = Modifier.padding(10.dp)) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text("価格を一括変更", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                            Switch(
                                checked = enablePrice,
                                onCheckedChange = { enablePrice = it },
                                modifier = Modifier.testTag("batch_edit_price_switch")
                            )
                        }
                        if (enablePrice) {
                            Spacer(modifier = Modifier.height(6.dp))
                            FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                FilterChip(
                                    selected = priceMode == BatchPriceMode.DELTA_YEN,
                                    onClick = { priceMode = BatchPriceMode.DELTA_YEN },
                                    label = { Text("±金額 (円)", fontSize = 11.sp) }
                                )
                                FilterChip(
                                    selected = priceMode == BatchPriceMode.DELTA_PERCENT,
                                    onClick = { priceMode = BatchPriceMode.DELTA_PERCENT },
                                    label = { Text("±割合 (%)", fontSize = 11.sp) }
                                )
                                FilterChip(
                                    selected = priceMode == BatchPriceMode.FIXED,
                                    onClick = { priceMode = BatchPriceMode.FIXED },
                                    label = { Text("固定額に設定", fontSize = 11.sp) }
                                )
                            }
                            if (priceMode != BatchPriceMode.FIXED) {
                                Spacer(modifier = Modifier.height(4.dp))
                                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                    FilterChip(
                                        selected = isNegativeDelta,
                                        onClick = { isNegativeDelta = true },
                                        label = { Text("値下げ (-)", fontSize = 11.sp) }
                                    )
                                    FilterChip(
                                        selected = !isNegativeDelta,
                                        onClick = { isNegativeDelta = false },
                                        label = { Text("値上げ (+)", fontSize = 11.sp) }
                                    )
                                }
                            }
                            Spacer(modifier = Modifier.height(6.dp))
                            OutlinedTextField(
                                value = priceValueText,
                                onValueChange = { input ->
                                    if (input.all { it.isDigit() }) priceValueText = input
                                },
                                label = {
                                    Text(
                                        when (priceMode) {
                                            BatchPriceMode.FIXED -> "統一価格 (円)"
                                            BatchPriceMode.DELTA_PERCENT -> if (isNegativeDelta) "値下げ率 (%)" else "値上げ率 (%)"
                                            else -> if (isNegativeDelta) "値下げ額 (円)" else "値上げ額 (円)"
                                        }
                                    )
                                },
                                singleLine = true,
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .testTag("batch_edit_price_input")
                            )
                            Text(
                                text = "※ 変更後の価格が 0円未満になる場合は 0円に自動調整されます",
                                fontSize = 10.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }

                // 適用前サンプルプレビュー
                if (sampleItem != null && hasAnyChange) {
                    val previewNewPrice = when (effectiveSpec.priceMode) {
                        BatchPriceMode.NONE -> sampleItem.price
                        BatchPriceMode.FIXED -> effectiveSpec.priceValue.coerceAtLeast(0)
                        BatchPriceMode.DELTA_YEN -> (sampleItem.price + effectiveSpec.priceValue).coerceAtLeast(0)
                        BatchPriceMode.DELTA_PERCENT -> (sampleItem.price + (sampleItem.price * effectiveSpec.priceValue) / 100).coerceAtLeast(0)
                    }
                    val beforeStatus = when (sampleItem.status) {
                        "SOLD" -> "売却済"
                        "DRAFT" -> "下書き"
                        else -> "出品中"
                    }
                    val afterStatus = when (effectiveSpec.newStatus ?: sampleItem.status) {
                        "SOLD" -> "売却済"
                        "DRAFT" -> "下書き"
                        else -> "出品中"
                    }
                    Card(
                        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.5f)),
                        shape = RoundedCornerShape(10.dp)
                    ) {
                        Column(modifier = Modifier.padding(10.dp)) {
                            Text(
                                text = "変更サンプル（先頭1件：${sampleItem.title.ifBlank { "無題" }}）",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.onPrimaryContainer
                            )
                            Spacer(modifier = Modifier.height(2.dp))
                            Text(
                                text = "状態：$beforeStatus ➔ $afterStatus ／ 価格：${formatter.format(sampleItem.price)} ➔ ${formatter.format(previewNewPrice)}",
                                fontSize = 11.sp,
                                color = MaterialTheme.colorScheme.onPrimaryContainer
                            )
                        }
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = { onConfirm(effectiveSpec) },
                enabled = hasAnyChange,
                modifier = Modifier.testTag("confirm_batch_edit_button")
            ) {
                Text("${selectedItems.size}件に適用")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("キャンセル")
            }
        }
    )
}
