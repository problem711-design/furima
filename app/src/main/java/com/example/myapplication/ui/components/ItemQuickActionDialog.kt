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
    val isBundle = item.mergedChildCount > 0 || item.bundleLevel > 0

    var showDeleteConfirm by remember { mutableStateOf(false) }
    var showUnmergeConfirm by remember { mutableStateOf(false) }

    if (showDeleteConfirm) {
        AlertDialog(
            onDismissRequest = { showDeleteConfirm = false },
            icon = { Icon(Icons.Default.Delete, contentDescription = null, tint = MaterialTheme.colorScheme.error) },
            title = { Text("商品を削除しますか？") },
            text = { Text("「${item.title}」を削除します。端末内の関連写真も削除されます。この操作は取り消せません。") },
            confirmButton = {
                Button(
                    onClick = {
                        showDeleteConfirm = false
                        onDelete()
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
                ) {
                    Text("削除する")
                }
            },
            dismissButton = {
                TextButton(onClick = { showDeleteConfirm = false }) {
                    Text("キャンセル")
                }
            }
        )
        return
    }

    if (showUnmergeConfirm) {
        AlertDialog(
            onDismissRequest = { showUnmergeConfirm = false },
            icon = { Icon(Icons.Default.CallSplit, contentDescription = null, tint = MaterialTheme.colorScheme.primary) },
            title = { Text("まとめ買い出品を解除（バラす）") },
            text = { Text("このまとめ出品を解除し、内包されている元の商品（${item.mergedChildCount}点）をすべて個別の『出品中』出品に戻しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        showUnmergeConfirm = false
                        onUnmerge()
                    }
                ) {
                    Text("バラして復元する")
                }
            },
            dismissButton = {
                TextButton(onClick = { showUnmergeConfirm = false }) {
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
                                .background(if (isSold) Color(0xFFC62828) else Color(0xFF2E7D32))
                                .padding(horizontal = 6.dp, vertical = 2.dp)
                        ) {
                            Text(
                                text = if (isSold) "売却済" else "出品中",
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
                QuickActionButton(
                    icon = if (isSold) Icons.Default.PublishedWithChanges else Icons.Default.CheckCircle,
                    label = if (isSold) "出品中に戻す" else "売却済みにする",
                    subtitle = if (isSold) "未売却として通常出品中へ戻します" else "売却完了として売上集計へ反映",
                    tint = if (isSold) MaterialTheme.colorScheme.primary else Color(0xFF2E7D32),
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
                        onClick = {
                            showUnmergeConfirm = true
                        }
                    )
                }

                // 5. 削除
                QuickActionButton(
                    icon = Icons.Default.Delete,
                    label = "商品を削除する",
                    subtitle = "端末の写真ファイルも含めて完全削除",
                    tint = MaterialTheme.colorScheme.error,
                    onClick = {
                        showDeleteConfirm = true
                    }
                )
            }
        },
        confirmButton = {},
        dismissButton = {
            TextButton(onClick = onDismiss) {
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
    onClick: () -> Unit
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
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
