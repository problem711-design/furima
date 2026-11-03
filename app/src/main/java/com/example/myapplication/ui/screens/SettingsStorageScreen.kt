package com.example.myapplication.ui.screens

import android.widget.Toast
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CleaningServices
import androidx.compose.material.icons.filled.DeleteSweep
import androidx.compose.material.icons.filled.PhotoSizeSelectLarge
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.ui.FleaMarketViewModel

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun SettingsStorageScreen(
    viewModel: FleaMarketViewModel
) {
    val context = LocalContext.current
    val currentSettings by viewModel.defaultSettings.collectAsState()

    var feeRateText by remember(currentSettings) { mutableStateOf(currentSettings.defaultFeeRatePercent.toString()) }
    var shippingCostText by remember(currentSettings) { mutableStateOf(currentSettings.defaultShippingCost.toString()) }
    var expenseCostText by remember(currentSettings) { mutableStateOf(currentSettings.defaultExpenseCost.toString()) }

    var thumbQuality by remember(currentSettings) { mutableFloatStateOf(currentSettings.thumbnailQuality.toFloat()) }
    var thumbDim by remember(currentSettings) { mutableStateOf(currentSettings.thumbnailMaxDimension) }
    var cleanupDays by remember(currentSettings) { mutableStateOf(currentSettings.autoCleanupSoldDays) }

    var showCleanupSharedDialog by remember { mutableStateOf(false) }
    var showCleanupSoldDialog by remember { mutableStateOf(false) }

    if (showCleanupSharedDialog) {
        AlertDialog(
            onDismissRequest = { showCleanupSharedDialog = false },
            icon = { Icon(Icons.Default.CleaningServices, contentDescription = null) },
            title = { Text("PC送信済み写真の最適化") },
            text = { Text("PCに送信済みの商品の元高画質写真を削除し、軽量サムネイルのみ残して容量を節約します。一覧の見た目は維持されます。実行しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        viewModel.cleanupSharedOriginalPhotos { count ->
                            showCleanupSharedDialog = false
                            Toast.makeText(context, "${count}枚の元写真を削除して容量を節約しました", Toast.LENGTH_SHORT).show()
                        }
                    }
                ) {
                    Text("実行する")
                }
            },
            dismissButton = {
                TextButton(onClick = { showCleanupSharedDialog = false }) { Text("キャンセル") }
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

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("設定 & ストレージ管理") },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.primary,
                    titleContentColor = MaterialTheme.colorScheme.onPrimary
                )
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
            // 1. サムネイル圧縮率・画質設定
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.PhotoSizeSelectLarge, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("🖼 サムネイル圧縮率・画質設定", fontWeight = FontWeight.Bold, fontSize = 16.sp)
                    }
                    Text(
                        text = "一覧表示用サムネイルの保存品質を調整できます。スマホの容量やカメラ性能に合わせて選択してください。",
                        fontSize = 12.sp,
                        color = Color.Gray,
                        modifier = Modifier.padding(top = 4.dp)
                    )

                    Spacer(modifier = Modifier.height(14.dp))

                    Text("① JPEG圧縮品質: ${thumbQuality.toInt()}%", fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
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

                    Spacer(modifier = Modifier.height(14.dp))

                    Text("② サムネイル最大サイズ", fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
                    Spacer(modifier = Modifier.height(4.dp))
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        FilterChip(selected = (thumbDim == 320), onClick = { thumbDim = 320 }, label = { Text("小 (320px・極小容量)", fontSize = 11.sp) })
                        FilterChip(selected = (thumbDim == 480), onClick = { thumbDim = 480 }, label = { Text("中 (480px・推奨)", fontSize = 11.sp) })
                        FilterChip(selected = (thumbDim == 640), onClick = { thumbDim = 640 }, label = { Text("大 (640px・高精細)", fontSize = 11.sp) })
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            // 2. ストレージ容量削減アクション
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.CleaningServices, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("🧹 スマホ容量クリーンアップ", fontWeight = FontWeight.Bold, fontSize = 16.sp)
                    }
                    Text(
                        text = "PCを母艦として転送済みの写真や、取引終了から時間が経った写真を整理できます。",
                        fontSize = 12.sp,
                        color = Color.Gray,
                        modifier = Modifier.padding(top = 4.dp)
                    )

                    Spacer(modifier = Modifier.height(14.dp))

                    Button(
                        onClick = { showCleanupSharedDialog = true },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text("PC送信済み写真の元画像を削除（サムネイル保持）")
                    }

                    Spacer(modifier = Modifier.height(10.dp))

                    OutlinedButton(
                        onClick = { showCleanupSoldDialog = true },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text("売却から${cleanupDays}日以上経過した写真を一括削除")
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            // 3. 基本費用設定
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(imageVector = Icons.Default.Settings, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(modifier = Modifier.width(8.dp))
                        Text("⚙️ 基本費用設定", fontWeight = FontWeight.Bold, fontSize = 16.sp)
                    }

                    Spacer(modifier = Modifier.height(12.dp))

                    OutlinedTextField(
                        value = feeRateText,
                        onValueChange = { feeRateText = it },
                        label = { Text("販売手数料率 (%)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    OutlinedTextField(
                        value = shippingCostText,
                        onValueChange = { shippingCostText = it },
                        label = { Text("標準送料 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Spacer(modifier = Modifier.height(10.dp))
                    OutlinedTextField(
                        value = expenseCostText,
                        onValueChange = { expenseCostText = it },
                        label = { Text("標準経費・梱包資材費 (円)") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                }
            }

            Spacer(modifier = Modifier.height(20.dp))

            Button(
                onClick = {
                    val fee = feeRateText.toIntOrNull() ?: 5
                    val shipping = shippingCostText.toIntOrNull() ?: 210
                    val expense = expenseCostText.toIntOrNull() ?: 55
                    viewModel.updateDefaultSettings(
                        DefaultSettings(
                            defaultFeeRatePercent = fee.coerceAtLeast(0),
                            defaultShippingCost = shipping.coerceAtLeast(0),
                            defaultExpenseCost = expense.coerceAtLeast(0),
                            thumbnailQuality = thumbQuality.toInt(),
                            thumbnailMaxDimension = thumbDim,
                            autoCleanupSoldDays = cleanupDays
                        )
                    )
                    Toast.makeText(context, "設定を保存しました", Toast.LENGTH_SHORT).show()
                },
                modifier = Modifier.fillMaxWidth().height(48.dp)
            ) {
                Text("設定を保存する", fontWeight = FontWeight.Bold)
            }
        }
    }
}
