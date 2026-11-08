package com.example.myapplication.ui.screens

import android.os.Build
import android.widget.Toast
import androidx.compose.foundation.background
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
import androidx.compose.material.icons.filled.Bookmark
import androidx.compose.material.icons.filled.CleaningServices
import androidx.compose.material.icons.filled.DeleteSweep
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.FileDownload
import androidx.compose.material.icons.filled.PhotoSizeSelectLarge
import androidx.compose.material.icons.filled.Settings
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.BuildConfig
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.FeePreset
import com.example.myapplication.ui.FleaMarketViewModel

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SettingsStorageScreen(
    viewModel: FleaMarketViewModel
) {
    val context = LocalContext.current
    val currentSettings by viewModel.defaultSettings.collectAsState()

    var activePresetId by remember(currentSettings) { mutableStateOf(currentSettings.activePresetId) }
    val presetsList = remember(currentSettings) {
        mutableStateListOf<FeePreset>().apply { addAll(currentSettings.presets) }
    }

    var feeRateText by remember(currentSettings) { mutableStateOf(currentSettings.defaultFeeRatePercent.toString()) }
    var shippingCostText by remember(currentSettings) { mutableStateOf(currentSettings.defaultShippingCost.toString()) }
    var expenseCostText by remember(currentSettings) { mutableStateOf(currentSettings.defaultExpenseCost.toString()) }

    var thumbQuality by remember(currentSettings) { mutableFloatStateOf(currentSettings.thumbnailQuality.toFloat()) }
    var thumbDim by remember(currentSettings) { mutableIntStateOf(currentSettings.thumbnailMaxDimension) }
    var cleanupDays by remember(currentSettings) { mutableIntStateOf(currentSettings.autoCleanupSoldDays) }

    var showCleanupSoldDialog by remember { mutableStateOf(false) }
    var showAddPresetDialog by remember { mutableStateOf(false) }
    var presetToDelete by remember { mutableStateOf<FeePreset?>(null) }

    // アプリバージョン情報の取得 (BuildConfig優先)
    val versionName = remember { BuildConfig.VERSION_NAME }
    val versionCode = remember { BuildConfig.VERSION_CODE }

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
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = newFeeText,
                        onValueChange = { newFeeText = it },
                        label = { Text("販売手数料率 (%)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = newShippingText,
                        onValueChange = { newShippingText = it },
                        label = { Text("送料 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = newExpenseText,
                        onValueChange = { newExpenseText = it },
                        label = { Text("梱包資材費・経費 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
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
                                feeRatePercent = newFeeText.toIntOrNull() ?: 10,
                                shippingCost = newShippingText.toIntOrNull() ?: 210,
                                expenseCost = newExpenseText.toIntOrNull() ?: 55
                            )
                            presetsList.add(newPreset)
                            activePresetId = newPreset.id
                            feeRateText = newPreset.feeRatePercent.toString()
                            shippingCostText = newPreset.shippingCost.toString()
                            expenseCostText = newPreset.expenseCost.toString()
                            showAddPresetDialog = false
                            Toast.makeText(context, "「${newPreset.name}」プリセットを追加しました", Toast.LENGTH_SHORT).show()
                        }
                    }
                ) {
                    Text("追加する")
                }
            },
            dismissButton = {
                TextButton(onClick = { showAddPresetDialog = false }) {
                    Text("キャンセル")
                }
            }
        )
    }



    if (showCleanupSoldDialog) {
        AlertDialog(
            onDismissRequest = { showCleanupSoldDialog = false },
            icon = { Icon(Icons.Default.DeleteSweep, contentDescription = null, tint = Color.Red) },
            title = { Text("売却後${cleanupDays}日経過写真の削除") },
            text = { Text("売却完了から${cleanupDays}日以上経過した商品の写真ファイルを完全削除します（売上記録は残ります）。実行しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        viewModel.cleanupSoldPhotosOlderThan(cleanupDays) { count ->
                            showCleanupSoldDialog = false
                            Toast.makeText(context, "${count}枚の古い取引写真を削除しました", Toast.LENGTH_SHORT).show()
                        }
                    }
                ) {
                    Text("削除する")
                }
            },
            dismissButton = {
                TextButton(onClick = { showCleanupSoldDialog = false }) { Text("キャンセル") }
            }
        )
    }

    presetToDelete?.let { preset ->
        AlertDialog(
            onDismissRequest = { presetToDelete = null },
            icon = { Icon(Icons.Default.Delete, contentDescription = null, tint = Color.Red) },
            title = { Text("プリセットの削除") },
            text = { Text("「${preset.name}」プリセットを削除しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        presetsList.remove(preset)
                        if (activePresetId == preset.id) {
                            val fallback = presetsList.firstOrNull() ?: DefaultSettings.defaultPresets.first()
                            activePresetId = fallback.id
                            feeRateText = fallback.feeRatePercent.toString()
                            shippingCostText = fallback.shippingCost.toString()
                            expenseCostText = fallback.expenseCost.toString()
                        }
                        presetToDelete = null
                        Toast.makeText(context, "プリセットを削除しました", Toast.LENGTH_SHORT).show()
                    }
                ) {
                    Text("削除する")
                }
            },
            dismissButton = {
                TextButton(onClick = { presetToDelete = null }) { Text("キャンセル") }
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
            // 1. 基本費用設定 ＆ プリセット機能
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
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Icon(imageVector = Icons.Default.Settings, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                            Spacer(modifier = Modifier.width(8.dp))
                            Text("⚙️ 基本費用設定 & プリセット", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                        }

                        IconButton(
                            onClick = { showAddPresetDialog = true },
                            modifier = Modifier.size(28.dp)
                        ) {
                            Icon(Icons.Default.Add, contentDescription = "プリセット追加", tint = MaterialTheme.colorScheme.primary)
                        }
                    }

                    Text(
                        text = "出品モール（Yahoo!フリマ、メルカリ、ラクマ等）に応じた手数料・送料プリセットを選択できます。",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        modifier = Modifier.padding(top = 4.dp, bottom = 8.dp)
                    )

                    // プリセット切り替えチップ
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        presetsList.forEach { preset ->
                            val isSelected = (activePresetId == preset.id)
                            val isDefault = DefaultSettings.defaultPresets.any { it.id == preset.id }
                            FilterChip(
                                selected = isSelected,
                                onClick = {
                                    activePresetId = preset.id
                                    feeRateText = preset.feeRatePercent.toString()
                                    shippingCostText = preset.shippingCost.toString()
                                    expenseCostText = preset.expenseCost.toString()
                                },
                                label = { Text(preset.name, fontSize = 11.sp, fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal) },
                                leadingIcon = if (isSelected) {
                                    { Icon(Icons.Default.Bookmark, contentDescription = null, modifier = Modifier.size(12.dp)) }
                                } else null,
                                trailingIcon = if (!isDefault) {
                                    {
                                        IconButton(
                                            onClick = { presetToDelete = preset },
                                            modifier = Modifier.size(16.dp)
                                        ) {
                                            Icon(Icons.Default.Close, contentDescription = "削除", modifier = Modifier.size(12.dp))
                                        }
                                    }
                                } else null
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(10.dp))

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

            // 2. 売上データのエクスポート (PC連携から移設)
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.FileDownload, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("📊 売上データのCSVエクスポート", fontWeight = FontWeight.Bold, fontSize = 15.sp)
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

            // 3. ストレージ容量クリーンアップ
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.CleaningServices, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("🧹 スマホ容量クリーンアップ", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                    Text(
                        text = "PCへ転送済みの高画質写真や、取引終了から時間が経った写真を整理してスマホの空き容量を確保します。",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f),
                        modifier = Modifier.padding(top = 4.dp, bottom = 12.dp)
                    )

                    OutlinedButton(
                        onClick = { showCleanupSoldDialog = true },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text("売却から${cleanupDays}日以上経過した写真を一括削除")
                    }
                }
            }

            // 4. サムネイル圧縮率・画質設定
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.PhotoSizeSelectLarge, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("🖼 サムネイル保存画質設定", fontWeight = FontWeight.Bold, fontSize = 15.sp)
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
                }
            }

            // 設定保存ボタン
            Button(
                onClick = {
                    val fee = feeRateText.toIntOrNull() ?: 5
                    val shipping = shippingCostText.toIntOrNull() ?: 210
                    val expense = expenseCostText.toIntOrNull() ?: 55

                    // アクティブプリセットの値を同期更新
                    val updatedPresets = presetsList.map { p ->
                        if (p.id == activePresetId) {
                            p.copy(feeRatePercent = fee, shippingCost = shipping, expenseCost = expense)
                        } else p
                    }

                    viewModel.updateDefaultSettings(
                        DefaultSettings(
                            defaultFeeRatePercent = fee.coerceAtLeast(0),
                            defaultShippingCost = shipping.coerceAtLeast(0),
                            defaultExpenseCost = expense.coerceAtLeast(0),
                            thumbnailQuality = thumbQuality.toInt(),
                            thumbnailMaxDimension = thumbDim,
                            autoCleanupSoldDays = cleanupDays,
                            activePresetId = activePresetId,
                            presets = updatedPresets
                        )
                    )
                    Toast.makeText(context, "設定を保存しました", Toast.LENGTH_SHORT).show()
                },
                modifier = Modifier.fillMaxWidth().height(48.dp),
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
