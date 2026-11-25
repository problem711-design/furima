package com.example.myapplication.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CloudDone
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.CloudSync
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.server.PcSyncProgress
import com.example.myapplication.data.server.SyncPhase
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@Composable
internal fun HeaderSyncStatusBadge(
    syncProgress: PcSyncProgress,
    isConnected: Boolean,
    unsyncedItemsCount: Int,
    onClick: () -> Unit
) {
    val isActiveSync = syncProgress.phase == SyncPhase.SYNCING_ITEMS ||
        syncProgress.phase == SyncPhase.UPLOADING_PHOTOS ||
        syncProgress.phase == SyncPhase.FETCHING_ITEMS

    val labelText: String
    val bgColor: Color
    val fgColor: Color
    when (syncProgress.phase) {
        SyncPhase.SYNCING_ITEMS -> {
            labelText = "同期 ${syncProgress.progressPercent}%"
            bgColor = Color(0xFFE0E7FF)
            fgColor = Color(0xFF3730A3)
        }
        SyncPhase.UPLOADING_PHOTOS -> {
            labelText = if (syncProgress.totalPhotoCount > 0) {
                "送信 ${syncProgress.progressPercent}% (${syncProgress.currentPhotoIndex}/${syncProgress.totalPhotoCount})"
            } else {
                "送信 ${syncProgress.progressPercent}%"
            }
            bgColor = Color(0xFFE0E7FF)
            fgColor = Color(0xFF3730A3)
        }
        SyncPhase.FETCHING_ITEMS -> {
            labelText = "取得 ${syncProgress.progressPercent}%"
            bgColor = Color(0xFFE0F2FE)
            fgColor = Color(0xFF075985)
        }
        SyncPhase.ERROR -> {
            labelText = "同期失敗 ⚠"
            bgColor = Color(0xFFFEE2E2)
            fgColor = Color(0xFFB91C1C)
        }
        SyncPhase.SUCCESS -> {
            labelText = "同期済 ✓"
            bgColor = Color(0xFFD1FAE5)
            fgColor = Color(0xFF065F46)
        }
        SyncPhase.IDLE -> {
            when {
                !isConnected -> {
                    labelText = "未接続"
                    bgColor = Color.White.copy(alpha = 0.20f)
                    fgColor = Color.White
                }
                unsyncedItemsCount > 0 -> {
                    labelText = "未同期 ${unsyncedItemsCount}件"
                    bgColor = Color(0xFFFEF3C7)
                    fgColor = Color(0xFF92400E)
                }
                else -> {
                    labelText = "同期済"
                    bgColor = Color(0xFFD1FAE5)
                    fgColor = Color(0xFF065F46)
                }
            }
        }
    }

    Box(
        contentAlignment = Alignment.Center,
        modifier = Modifier
            .minimumInteractiveComponentSize()
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClick = onClick)
            .testTag("header_sync_status_badge")
    ) {
        Surface(
            color = bgColor,
            shape = RoundedCornerShape(12.dp)
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(4.dp),
                modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp)
            ) {
                if (isActiveSync) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(11.dp),
                        strokeWidth = 1.8.dp,
                        color = fgColor
                    )
                } else {
                    val iconVec = when {
                        syncProgress.phase == SyncPhase.ERROR -> Icons.Default.ErrorOutline
                        syncProgress.phase == SyncPhase.SUCCESS -> Icons.Default.CloudDone
                        !isConnected -> Icons.Default.CloudOff
                        unsyncedItemsCount > 0 -> Icons.Default.CloudSync
                        else -> Icons.Default.CloudDone
                    }
                    Icon(
                        imageVector = iconVec,
                        contentDescription = null,
                        tint = fgColor,
                        modifier = Modifier.size(13.dp)
                    )
                }
                Text(
                    text = labelText,
                    color = fgColor,
                    fontSize = 10.sp,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.widthIn(max = 128.dp)
                )
            }
        }
    }
}

@Composable
internal fun SyncStatusDetailDialog(
    syncProgress: PcSyncProgress,
    isConnected: Boolean,
    serverUrl: String,
    unsyncedItemsCount: Int,
    lastSyncSuccessAt: Long?,
    onRetrySync: () -> Unit,
    onDismiss: () -> Unit
) {
    val isActiveSync = syncProgress.phase == SyncPhase.SYNCING_ITEMS ||
        syncProgress.phase == SyncPhase.UPLOADING_PHOTOS ||
        syncProgress.phase == SyncPhase.FETCHING_ITEMS
    val timeFormatter = remember { SimpleDateFormat("yyyy/MM/dd HH:mm:ss", Locale.getDefault()) }
    val lastSyncText = lastSyncSuccessAt?.let { timeFormatter.format(Date(it)) } ?: "まだ同期されていません"

    fun formatByteSize(bytes: Long): String {
        if (bytes < 1024L) return "${bytes} B"
        val kb = bytes / 1024.0
        if (kb < 1024.0) return String.format(Locale.US, "%.1f KB", kb)
        val mb = kb / 1024.0
        return String.format(Locale.US, "%.2f MB", mb)
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("PCサーバー同期ステータス", fontWeight = FontWeight.Bold, fontSize = 16.sp) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    text = when {
                        serverUrl.isBlank() -> "接続先PC: 未設定（PC連携タブからQR読取してください）"
                        isConnected -> "接続状態: 接続中\nURL: $serverUrl"
                        else -> "接続状態: 未接続\nURL: $serverUrl"
                    },
                    fontSize = 12.sp
                )
                Text(
                    text = "最終同期完了: $lastSyncText",
                    fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                if (unsyncedItemsCount > 0) {
                    Text(
                        text = "未送信のローカル写真を含む商品: ${unsyncedItemsCount}件",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color(0xFFB45309)
                    )
                }

                if (syncProgress.detailMessage.isNotBlank()) {
                    Text(
                        text = "状態: ${syncProgress.detailMessage}",
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Medium
                    )
                }

                if (isActiveSync) {
                    LinearProgressIndicator(
                        progress = { (syncProgress.progressPercent / 100f).coerceIn(0f, 1f) },
                        modifier = Modifier.fillMaxWidth()
                    )
                    val progressSubText = buildString {
                        append("${syncProgress.progressPercent}%")
                        if (syncProgress.totalPhotoCount > 0) {
                            append(" (${syncProgress.currentPhotoIndex}/${syncProgress.totalPhotoCount}枚)")
                        }
                        if (syncProgress.totalBytes > 0L) {
                            append(" ・ ${formatByteSize(syncProgress.bytesSent)} / ${formatByteSize(syncProgress.totalBytes)}")
                        }
                    }
                    Text(
                        text = progressSubText,
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.primary
                    )
                }

                if (!syncProgress.errorDetail.isNullOrBlank()) {
                    Surface(
                        color = MaterialTheme.colorScheme.errorContainer,
                        shape = RoundedCornerShape(8.dp),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text(
                            text = "エラー詳細:\n${syncProgress.errorDetail}",
                            color = MaterialTheme.colorScheme.onErrorContainer,
                            fontSize = 11.sp,
                            modifier = Modifier.padding(10.dp)
                        )
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    onDismiss()
                    onRetrySync()
                },
                enabled = serverUrl.isNotBlank() && !isActiveSync,
                modifier = Modifier.testTag("sync_status_retry_button")
            ) {
                Icon(Icons.Default.Refresh, contentDescription = null, modifier = Modifier.size(16.dp))
                Spacer(modifier = Modifier.width(4.dp))
                Text("今すぐ再同期")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("閉じる")
            }
        }
    )
}

