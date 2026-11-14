package com.example.myapplication.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CallSplit
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.PublishedWithChanges
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import com.example.myapplication.data.local.ItemWithPhotos
import java.io.File
import java.text.NumberFormat
import java.util.Locale

@Composable
fun ItemQuickActionDialog(
    itemWithPhotos: ItemWithPhotos,
    onDismiss: () -> Unit,
    onToggleStatus: () -> Unit,
    onNavigateToEdit: () -> Unit,
    onCopyText: () -> Unit,
    onUnmerge: () -> Unit,
    onDelete: () -> Unit
) {
    val haptic = LocalHapticFeedback.current
    val item = itemWithPhotos.item
    val firstPhoto = itemWithPhotos.photos.firstOrNull()
    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)
    val isSold = item.status == "SOLD"
    val isDraft = item.status == "DRAFT"
    val isBundle = item.mergedChildCount > 0 || item.bundleLevel > 0

    val statusBadgeColor = when (item.status) {
        "SOLD" -> Color(0xFFC62828)
        "MERGED" -> Color(0xFF6A1B9A)
        "DRAFT" -> Color.Gray
        else -> Color(0xFF2E7D32)
    }
    val statusBadgeLabel = when (item.status) {
        "SOLD" -> "売却済"
        "MERGED" -> "まとめ済"
        "DRAFT" -> "下書き"
        else -> "出品中"
    }

    var showDeleteConfirm by remember(item.id) { mutableStateOf(false) }
    var showUnmergeConfirm by remember(item.id) { mutableStateOf(false) }
    var isSubmitting by remember(item.id) { mutableStateOf(false) }

    if (showDeleteConfirm) {
        AlertDialog(
            onDismissRequest = { if (!isSubmitting) showDeleteConfirm = false },
            icon = { Icon(Icons.Default.Delete, contentDescription = null, tint = MaterialTheme.colorScheme.error) },
            title = { Text("ゴミ箱へ移動しますか？") },
            text = { Text("「${item.title}」をゴミ箱へ移動します（30日間保管され、いつでも元に戻せます）。") },
            confirmButton = {
                Button(
                    onClick = {
                        if (!isSubmitting) {
                            isSubmitting = true
                            showDeleteConfirm = false
                            onDismiss()
                            onDelete()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                    modifier = Modifier.testTag("quick_action_confirm_delete")
                ) {
                    Text("ゴミ箱へ移動")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { showDeleteConfirm = false },
                    modifier = Modifier.testTag("quick_action_cancel_delete")
                ) {
                    Text("キャンセル")
                }
            }
        )
        return
    }

    if (showUnmergeConfirm) {
        AlertDialog(
            onDismissRequest = { if (!isSubmitting) showUnmergeConfirm = false },
            icon = { Icon(Icons.Default.CallSplit, contentDescription = null, tint = MaterialTheme.colorScheme.primary) },
            title = { Text("まとめ買い出品を解除（バラす）") },
            text = { Text("このまとめ出品を解除し、内包されている元の商品（${item.mergedChildCount}点）をすべて個別の『出品中』出品に戻しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        if (!isSubmitting) {
                            isSubmitting = true
                            showUnmergeConfirm = false
                            onDismiss()
                            onUnmerge()
                        }
                    },
                    modifier = Modifier.testTag("quick_action_confirm_unmerge")
                ) {
                    Text("バラして復元する")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { showUnmergeConfirm = false },
                    modifier = Modifier.testTag("quick_action_cancel_unmerge")
                ) {
                    Text("キャンセル")
                }
            }
        )
        return
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                // サムネイル画像
                val displayFile = firstPhoto?.displayFile
                Box(
                    modifier = Modifier
                        .size(52.dp)
                        .clip(RoundedCornerShape(8.dp))
                        .background(MaterialTheme.colorScheme.surfaceVariant),
                    contentAlignment = Alignment.Center
                ) {
                    if (displayFile != null && displayFile.exists()) {
                        AsyncImage(
                            model = displayFile,
                            contentDescription = item.title,
                            contentScale = ContentScale.Crop,
                            modifier = Modifier.fillMaxSize()
                        )
                    } else {
                        Text("No Photo", fontSize = 10.sp, color = Color.Gray)
                    }
                }

                Spacer(modifier = Modifier.width(12.dp))

                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = item.title.ifBlank { "無題の商品" },
                        fontWeight = FontWeight.Bold,
                        fontSize = 15.sp,
                        maxLines = 1
                    )
                    Spacer(modifier = Modifier.height(2.dp))
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = formatter.format(if (isSold && item.soldPrice > 0) item.soldPrice else item.price),
                            fontWeight = FontWeight.Bold,
                            fontSize = 14.sp,
                            color = MaterialTheme.colorScheme.primary
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(4.dp))
                                .background(statusBadgeColor)
                                .padding(horizontal = 6.dp, vertical = 2.dp)
                                .testTag("quick_action_status_badge")
                        ) {
                            Text(
                                text = statusBadgeLabel,
                                color = Color.White,
                                fontSize = 10.sp,
                                fontWeight = FontWeight.Bold
                            )
                        }
                    }
                }
            }
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                HorizontalDivider()
                Spacer(modifier = Modifier.height(4.dp))

                // 1. ステータス切り替え
                val toggleIcon = when {
                    isSold || isDraft -> Icons.Default.PublishedWithChanges
                    else -> Icons.Default.CheckCircle
                }
                val toggleLabel = when {
                    isSold -> "出品中に戻す"
                    isDraft -> "出品中にする"
                    else -> "売却済みにする"
                }
                val toggleSubtitle = when {
                    isSold -> "未売却として通常出品中へ戻します"
                    isDraft -> "下書きから通常出品中へ公開します"
                    else -> "売却完了として売上集計へ反映"
                }
                val toggleTint = when {
                    isSold -> MaterialTheme.colorScheme.primary
                    else -> Color(0xFF2E7D32)
                }

                QuickActionButton(
                    icon = toggleIcon,
                    label = toggleLabel,
                    subtitle = toggleSubtitle,
                    tint = toggleTint,
                    testTagName = "quick_action_toggle_status",
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        onToggleStatus()
                        onDismiss()
                    }
                )

                // 2. 商品情報の編集
                QuickActionButton(
                    icon = Icons.Default.Edit,
                    label = "商品情報を編集する",
                    subtitle = "写真・価格・説明文・個別費用の変更",
                    tint = MaterialTheme.colorScheme.primary,
                    testTagName = "quick_action_edit",
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        onDismiss()
                        onNavigateToEdit()
                    }
                )

                // 3. 出品用テキストをコピー
                QuickActionButton(
                    icon = Icons.Default.ContentCopy,
                    label = "出品用テキストをコピー",
                    subtitle = "メルカリ等の出品フォームへすぐ貼り付け",
                    tint = MaterialTheme.colorScheme.primary,
                    testTagName = "quick_action_copy",
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        onCopyText()
                        onDismiss()
                    }
                )

                // 4. まとめ買いの解除 (まとめ商品の場合のみ表示)
                if (isBundle) {
                    QuickActionButton(
                        icon = Icons.Default.CallSplit,
                        label = "まとめ買いを解除（バラす）",
                        subtitle = "内包する${item.mergedChildCount}点の商品を個別出品に復元",
                        tint = Color(0xFFE65100),
                        testTagName = "quick_action_unmerge",
                        onClick = {
                            showUnmergeConfirm = true
                        }
                    )
                }

                // 5. ゴミ箱へ移動
                QuickActionButton(
                    icon = Icons.Default.Delete,
                    label = "ゴミ箱へ移動する",
                    subtitle = "30日間保管され、ゴミ箱画面から復元可能",
                    tint = MaterialTheme.colorScheme.error,
                    testTagName = "quick_action_delete",
                    onClick = {
                        showDeleteConfirm = true
                    }
                )
            }
        },
        confirmButton = {},
        dismissButton = {
            TextButton(
                onClick = onDismiss,
                modifier = Modifier.testTag("quick_action_close")
            ) {
                Text("閉じる")
            }
        }
    )
}

@Composable
private fun QuickActionButton(
    icon: ImageVector,
    label: String,
    subtitle: String,
    tint: Color,
    testTagName: String,
    onClick: () -> Unit
) {
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .testTag(testTagName),
        onClick = onClick,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f)),
        shape = RoundedCornerShape(10.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(36.dp)
                    .clip(RoundedCornerShape(8.dp))
                    .background(tint.copy(alpha = 0.12f)),
                contentAlignment = Alignment.Center
            ) {
                Icon(imageVector = icon, contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
            }
            Spacer(modifier = Modifier.width(12.dp))
            Column {
                Text(text = label, fontWeight = FontWeight.Bold, fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurface)
                Text(text = subtitle, fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}
