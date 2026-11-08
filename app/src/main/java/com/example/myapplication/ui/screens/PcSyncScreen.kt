package com.example.myapplication.ui.screens

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.widget.Toast
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Dns
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.filled.Wifi
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.ui.FleaMarketViewModel

@Composable
fun PcSyncScreen(
    viewModel: FleaMarketViewModel
) {
    val isServerRunning by viewModel.isWebServerRunning.collectAsState()
    val serverIp by viewModel.serverIpAddress.collectAsState()
    val serverLogs by viewModel.webServerLogs.collectAsState()

    val context = LocalContext.current
    val snackbarHostState = remember { SnackbarHostState() }

    var showLogsDialog by remember { mutableStateOf(false) }

    val serverUrl = remember(serverIp, viewModel.serverPort) {
        if (!serverIp.isNullOrBlank()) "http://$serverIp:${viewModel.serverPort}" else null
    }

    if (showLogsDialog) {
        AlertDialog(
            onDismissRequest = { showLogsDialog = false },
            icon = { Icon(Icons.Default.Dns, contentDescription = null) },
            title = { Text("Webサーバー アクセスログ") },
            text = {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(280.dp)
                ) {
                    if (serverLogs.isEmpty()) {
                        Text(
                            text = "ログはまだありません",
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f),
                            fontSize = 13.sp
                        )
                    } else {
                        LazyColumn(
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(280.dp),
                            verticalArrangement = Arrangement.spacedBy(4.dp)
                        ) {
                            items(serverLogs.reversed()) { log ->
                                Text(
                                    text = log,
                                    fontSize = 11.sp,
                                    fontFamily = FontFamily.Monospace,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                            }
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = { showLogsDialog = false }) {
                    Text("閉じる")
                }
            }
        )
    }

    Scaffold(
        topBar = {
            Surface(
                color = MaterialTheme.colorScheme.primaryContainer,
                shadowElevation = 2.dp
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 14.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Icon(
                        imageVector = Icons.Default.Computer,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onPrimaryContainer,
                        modifier = Modifier.size(20.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "PC連携・管理",
                        fontWeight = FontWeight.Bold,
                        fontSize = 17.sp,
                        color = MaterialTheme.colorScheme.onPrimaryContainer
                    )
                }
            }
        },
        snackbarHost = { SnackbarHost(snackbarHostState) }
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .padding(innerPadding)
                .fillMaxWidth()
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // PCブラウザ連携 (Webサーバー) カード
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(
                    containerColor = if (isServerRunning) MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.35f)
                    else MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)
                ),
                border = CardDefaults.outlinedCardBorder().copy(
                    brush = androidx.compose.ui.graphics.SolidColor(
                        if (isServerRunning) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outlineVariant
                    )
                )
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Box(
                                modifier = Modifier
                                    .size(36.dp)
                                    .background(
                                        if (isServerRunning) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceVariant,
                                        CircleShape
                                    ),
                                contentAlignment = Alignment.Center
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Language,
                                    contentDescription = null,
                                    tint = if (isServerRunning) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant,
                                    modifier = Modifier.size(20.dp)
                                )
                            }
                            Spacer(modifier = Modifier.width(12.dp))
                            Column {
                                Text(
                                    text = "PCブラウザ連携",
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 16.sp
                                )
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    Box(
                                        modifier = Modifier
                                            .size(8.dp)
                                            .background(
                                                if (isServerRunning) Color(0xFF10B981) else Color(0xFF94A3B8),
                                                CircleShape
                                            )
                                    )
                                    Spacer(modifier = Modifier.width(6.dp))
                                    Text(
                                        text = if (isServerRunning) "Webサーバー稼働中" else "サーバー停止中",
                                        fontSize = 12.sp,
                                        fontWeight = FontWeight.Medium,
                                        color = if (isServerRunning) Color(0xFF047857) else MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                }
                            }
                        }

                        Switch(
                            checked = isServerRunning,
                            onCheckedChange = { enabled ->
                                viewModel.toggleWebServer(enabled)
                            }
                        )
                    }

                    Spacer(modifier = Modifier.height(12.dp))
                    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
                    Spacer(modifier = Modifier.height(12.dp))

                    AnimatedVisibility(visible = isServerRunning) {
                        Column {
                            Text(
                                text = "PCのブラウザで以下のアドレスを開いてください：",
                                fontSize = 12.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                            Spacer(modifier = Modifier.height(6.dp))

                            // URL表示・コピーカード
                            Card(
                                modifier = Modifier.fillMaxWidth(),
                                colors = CardDefaults.cardColors(
                                    containerColor = MaterialTheme.colorScheme.surface
                                ),
                                shape = RoundedCornerShape(10.dp)
                            ) {
                                Row(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(horizontal = 12.dp, vertical = 8.dp),
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.SpaceBetween
                                ) {
                                    Text(
                                        text = serverUrl ?: "IPアドレスを取得中...",
                                        fontWeight = FontWeight.Bold,
                                        fontSize = 16.sp,
                                        fontFamily = FontFamily.Monospace,
                                        color = MaterialTheme.colorScheme.primary
                                    )

                                    Row {
                                        IconButton(
                                            onClick = { viewModel.refreshServerIp() },
                                            modifier = Modifier.size(32.dp)
                                        ) {
                                            Icon(
                                                imageVector = Icons.Default.Refresh,
                                                contentDescription = "再取得",
                                                modifier = Modifier.size(18.dp)
                                            )
                                        }
                                        if (serverUrl != null) {
                                            IconButton(
                                                onClick = {
                                                    val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                                                    clipboard.setPrimaryClip(ClipData.newPlainText("PC Sync URL", serverUrl))
                                                    Toast.makeText(context, "URLをコピーしました", Toast.LENGTH_SHORT).show()
                                                },
                                                modifier = Modifier.size(32.dp)
                                            ) {
                                                Icon(
                                                    imageVector = Icons.Default.ContentCopy,
                                                    contentDescription = "コピー",
                                                    modifier = Modifier.size(18.dp)
                                                )
                                            }
                                        }
                                    }
                                }
                            }

                            Spacer(modifier = Modifier.height(8.dp))
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Icon(
                                    imageVector = Icons.Default.Wifi,
                                    contentDescription = null,
                                    tint = MaterialTheme.colorScheme.primary,
                                    modifier = Modifier.size(14.dp)
                                )
                                Spacer(modifier = Modifier.width(4.dp))
                                Text(
                                    text = "※ スマホとPCが同一Wi-Fiまたはテザリング接続されている必要があります",
                                    fontSize = 11.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }

                            Spacer(modifier = Modifier.height(8.dp))
                            TextButton(
                                onClick = { showLogsDialog = true },
                                contentPadding = PaddingValues(0.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Dns,
                                    contentDescription = null,
                                    modifier = Modifier.size(16.dp)
                                )
                                Spacer(modifier = Modifier.width(4.dp))
                                Text("アクセスログを見る (${serverLogs.size}件)", fontSize = 12.sp)
                            }
                        }
                    }

                    AnimatedVisibility(visible = !isServerRunning) {
                        Text(
                            text = "スイッチをONにすると、スマホ内にWebサーバーが起動します。PCブラウザから商品の写真閲覧・新規出品・編集・説明文コピーが可能になります。",
                            fontSize = 13.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.85f),
                            lineHeight = 18.sp
                        )
                    }
                }
            }

            // 母艦PCサーバーへの差分同期＆自動ストレージ解放カード
            val pcServerUrl by viewModel.pcServerUrl.collectAsState()
            val isPcConnected by viewModel.isPcServerConnected.collectAsState()
            var inputUrl by remember(pcServerUrl) { mutableStateOf(pcServerUrl) }
            var isSyncing by remember { mutableStateOf(false) }
            var missingOriginalCount by remember { mutableStateOf<Int?>(null) }

            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.surface
                ),
                border = CardDefaults.outlinedCardBorder()
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Icon(
                                imageVector = Icons.Default.Computer,
                                contentDescription = null,
                                tint = MaterialTheme.colorScheme.primary,
                                modifier = Modifier.size(22.dp)
                            )
                            Text(
                                text = "母艦PCサーバー連携 (第2世代)",
                                fontWeight = FontWeight.Bold,
                                fontSize = 15.sp
                            )
                        }

                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Box(
                                modifier = Modifier
                                    .size(8.dp)
                                    .background(
                                        if (isPcConnected) Color(0xFF10B981) else Color(0xFF94A3B8),
                                        CircleShape
                                    )
                            )
                            Spacer(modifier = Modifier.width(4.dp))
                            Text(
                                text = if (isPcConnected) "接続中" else "未接続",
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Medium,
                                color = if (isPcConnected) Color(0xFF047857) else MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(8.dp))
                    Text(
                        text = "PC側で起動中の furima-server.exe のアドレスを指定し、差分写真の高速送信とスマホ側の自動容量削減を行います。",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )

                    Spacer(modifier = Modifier.height(12.dp))
                    androidx.compose.material3.OutlinedTextField(
                        value = inputUrl,
                        onValueChange = {
                            inputUrl = it
                            viewModel.updatePcServerUrl(it)
                        },
                        label = { Text("PCサーバーURL") },
                        placeholder = { Text("http://192.168.1.10:8080") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )

                    missingOriginalCount?.let { count ->
                        if (count > 0) {
                            Spacer(modifier = Modifier.height(8.dp))
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .background(Color(0xFFFFF7ED), RoundedCornerShape(8.dp))
                                    .padding(horizontal = 10.dp, vertical = 6.dp)
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Warning,
                                    contentDescription = null,
                                    tint = Color(0xFFEA580C),
                                    modifier = Modifier.size(16.dp)
                                )
                                Spacer(modifier = Modifier.width(6.dp))
                                Text(
                                    text = "原本なし写真: ${count}件（PC保存用フォルダに存在しません）",
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = Color(0xFFC2410C)
                                )
                            }
                        }
                    }

                    Spacer(modifier = Modifier.height(12.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        TextButton(
                            onClick = {
                                viewModel.testPcServerConnection { connected ->
                                    val msg = if (connected) "PCサーバーへの接続に成功しました！" else "PCサーバーへ接続できませんでした。"
                                    Toast.makeText(context, msg, Toast.LENGTH_SHORT).show()
                                }
                            },
                            modifier = Modifier.weight(1f)
                        ) {
                            Text("接続確認")
                        }

                        androidx.compose.material3.Button(
                            onClick = {
                                isSyncing = true
                                viewModel.syncDeltaToPcServer { result ->
                                    isSyncing = false
                                    missingOriginalCount = result.missingOriginalPhotosCount
                                    if (result.isSuccess) {
                                        val freedStr = if (result.freedMb > 0) " (${result.freedMb}MB空けました)" else ""
                                        val missingStr = if (result.missingOriginalPhotosCount > 0) " (原本なし: ${result.missingOriginalPhotosCount}件)" else ""
                                        Toast.makeText(
                                            context,
                                            "同期成功: ${result.syncedItemsCount}件の商品メタデータ・${result.syncedPhotosCount}枚の新規写真をPCへ送信しました$freedStr$missingStr",
                                            Toast.LENGTH_LONG
                                        ).show()
                                    } else {
                                        Toast.makeText(
                                            context,
                                            "同期失敗: ${result.errorMessage ?: "通信エラー"}",
                                            Toast.LENGTH_LONG
                                        ).show()
                                    }
                                }
                            },
                            enabled = !isSyncing,
                            modifier = Modifier.weight(1.5f)
                        ) {
                            Text(if (isSyncing) "同期中..." else "差分同期＆容量解放")
                        }
                    }
                }
            }
        }
    }
}
