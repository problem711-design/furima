package com.example.myapplication.ui.screens

import android.widget.Toast
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.DeleteForever
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.RestoreFromTrash
import androidx.compose.material3.Checkbox
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.ui.FleaMarketViewModel
import java.text.NumberFormat
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TrashScreen(
    viewModel: FleaMarketViewModel,
    onNavigateBack: () -> Unit
) {
    val context = LocalContext.current
    val trashedItems by viewModel.trashedItemsWithPhotos.collectAsState()
    var confirmPermanentItem by remember { mutableStateOf<ItemWithPhotos?>(null) }
    var showEmptyTrashConfirm by remember { mutableStateOf(false) }
    var isSelectionMode by remember { mutableStateOf(false) }
    var selectedIds by remember { mutableStateOf<Set<Int>>(emptySet()) }
    var showBatchPermanentConfirm by remember { mutableStateOf(false) }

    val formatter = remember { NumberFormat.getCurrencyInstance(Locale.JAPAN) }
    val dateFormat = remember { SimpleDateFormat("yyyy/MM/dd HH:mm", Locale.JAPAN) }

    confirmPermanentItem?.let { target ->
        AlertDialog(
            onDismissRequest = { confirmPermanentItem = null },
            icon = {
                Icon(
                    imageVector = Icons.Default.DeleteForever,
                    contentDescription = "完全削除",
                    tint = MaterialTheme.colorScheme.error
                )
            },
            title = { Text("完全に削除しますか？") },
            text = {
                Text(
                    "「${target.item.title.ifBlank { "無題の商品" }}」を完全に削除します。他商品で共有されていない端末内の写真データも完全に消去され、元に戻すことはできません。"
                )
            },
            confirmButton = {
                Button(
                    onClick = {
                        val targetItem = target
                        confirmPermanentItem = null
                        viewModel.permanentlyDeleteItem(targetItem) {
                            Toast.makeText(context, "商品を完全に削除しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.error,
                        contentColor = MaterialTheme.colorScheme.onError
                    ),
                    modifier = Modifier
                        .minimumInteractiveComponentSize()
                        .testTag("confirm_permanent_delete_trash_item")
                ) {
                    Text("完全削除")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { confirmPermanentItem = null },
                    modifier = Modifier.minimumInteractiveComponentSize()
                ) {
                    Text("キャンセル")
                }
            }
        )
    }

    if (showEmptyTrashConfirm) {
        AlertDialog(
            onDismissRequest = { showEmptyTrashConfirm = false },
            icon = {
                Icon(
                    imageVector = Icons.Default.DeleteForever,
                    contentDescription = "ゴミ箱を空にする",
                    tint = MaterialTheme.colorScheme.error
                )
            },
            title = { Text("ゴミ箱を空にしますか？") },
            text = {
                Text("ゴミ箱内の ${trashedItems.size} 件の商品をすべて完全に削除します。この操作は取り消せません。")
            },
            confirmButton = {
                Button(
                    onClick = {
                        showEmptyTrashConfirm = false
                        viewModel.emptyTrash { count ->
                            Toast.makeText(context, "ゴミ箱から ${count} 件を完全削除しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.error,
                        contentColor = MaterialTheme.colorScheme.onError
                    ),
                    modifier = Modifier
                        .minimumInteractiveComponentSize()
                        .testTag("confirm_empty_trash_button")
                ) {
                    Text("すべて完全削除")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { showEmptyTrashConfirm = false },
                    modifier = Modifier.minimumInteractiveComponentSize()
                ) {
                    Text("キャンセル")
                }
            }
        )
    }

    if (showBatchPermanentConfirm && selectedIds.isNotEmpty()) {
        val targetIds = selectedIds.toList()
        AlertDialog(
            onDismissRequest = { showBatchPermanentConfirm = false },
            icon = {
                Icon(
                    imageVector = Icons.Default.DeleteForever,
                    contentDescription = "一括完全削除",
                    tint = MaterialTheme.colorScheme.error
                )
            },
            title = { Text("${targetIds.size}件を完全に削除しますか？") },
            text = {
                Text("選択した ${targetIds.size}件の商品を完全に削除します。他商品で共有されていない写真も消去され、元に戻せません。")
            },
            confirmButton = {
                Button(
                    onClick = {
                        showBatchPermanentConfirm = false
                        viewModel.batchPermanentlyDeleteFromTrash(targetIds) { count ->
                            selectedIds = emptySet()
                            isSelectionMode = false
                            Toast.makeText(context, "${count}件を完全に削除しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(
                        containerColor = MaterialTheme.colorScheme.error,
                        contentColor = MaterialTheme.colorScheme.onError
                    ),
                    modifier = Modifier.testTag("confirm_batch_permanent_delete_trash")
                ) {
                    Text("完全削除")
                }
            },
            dismissButton = {
                TextButton(onClick = { showBatchPermanentConfirm = false }) {
                    Text("キャンセル")
                }
            }
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        text = "ゴミ箱 (${trashedItems.size}件)",
                        fontWeight = FontWeight.Bold
                    )
                },
                navigationIcon = {
                    IconButton(
                        onClick = onNavigateBack,
                        modifier = Modifier
                            .minimumInteractiveComponentSize()
                            .testTag("trash_back_button")
                    ) {
                        Icon(
                            imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                            contentDescription = "戻る"
                        )
                    }
                },
                actions = {
                    if (trashedItems.isNotEmpty()) {
                        IconButton(
                            onClick = {
                                if (isSelectionMode) {
                                    isSelectionMode = false
                                    selectedIds = emptySet()
                                } else {
                                    isSelectionMode = true
                                }
                            },
                            modifier = Modifier
                                .minimumInteractiveComponentSize()
                                .testTag("trash_selection_toggle_button")
                        ) {
                            Icon(
                                imageVector = if (isSelectionMode) Icons.Default.Close else Icons.Default.Checklist,
                                contentDescription = if (isSelectionMode) "選択を終了" else "複数選択"
                            )
                        }
                        if (isSelectionMode) {
                            TextButton(
                                onClick = {
                                    selectedIds = if (selectedIds.size == trashedItems.size) {
                                        emptySet()
                                    } else {
                                        trashedItems.map { it.item.id }.toSet()
                                    }
                                },
                                modifier = Modifier.minimumInteractiveComponentSize()
                            ) {
                                Text(if (selectedIds.size == trashedItems.size) "全解除" else "全選択", fontWeight = FontWeight.Bold)
                            }
                        } else {
                            TextButton(
                                onClick = { showEmptyTrashConfirm = true },
                                modifier = Modifier
                                    .minimumInteractiveComponentSize()
                                    .testTag("empty_trash_button")
                            ) {
                                Text(
                                    text = "空にする",
                                    color = MaterialTheme.colorScheme.error,
                                    fontWeight = FontWeight.Bold
                                )
                            }
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface,
                    titleContentColor = MaterialTheme.colorScheme.onSurface
                )
            )
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .background(MaterialTheme.colorScheme.background)
        ) {
            Surface(
                color = MaterialTheme.colorScheme.secondaryContainer.copy(alpha = 0.6f),
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                shape = RoundedCornerShape(12.dp)
            ) {
                Row(
                    modifier = Modifier.padding(12.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Icon(
                        imageVector = Icons.Default.Info,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onSecondaryContainer,
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = "ゴミ箱に入れた商品は30日後に自動で完全削除されます。それまではいつでも元の出品一覧へ復元できます。",
                        fontSize = 12.sp,
                        lineHeight = 16.sp,
                        color = MaterialTheme.colorScheme.onSecondaryContainer
                    )
                }
            }

            if (trashedItems.isEmpty()) {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center
                ) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        modifier = Modifier.padding(24.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Default.DeleteOutline,
                            contentDescription = "空のゴミ箱",
                            tint = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.5f),
                            modifier = Modifier.size(56.dp)
                        )
                        Spacer(modifier = Modifier.height(12.dp))
                        Text(
                            text = "ゴミ箱は空です",
                            style = MaterialTheme.typography.titleMedium,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "削除した商品はここに30日間保管され、いつでも復元できます。",
                            fontSize = 12.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.8f)
                        )
                    }
                }
            } else {
                val now = System.currentTimeMillis()
                Box(modifier = Modifier.weight(1f).fillMaxWidth()) {
                LazyColumn(
                    contentPadding = PaddingValues(
                        start = 12.dp,
                        end = 12.dp,
                        top = 6.dp,
                        bottom = if (isSelectionMode && selectedIds.isNotEmpty()) 88.dp else 12.dp
                    ),
                    verticalArrangement = Arrangement.spacedBy(10.dp),
                    modifier = Modifier.fillMaxSize()
                ) {
                    items(trashedItems, key = { it.item.id }) { itemWithPhotos ->
                        val item = itemWithPhotos.item
                        val firstPhoto = itemWithPhotos.photos.firstOrNull()
                        val displayFile = firstPhoto?.displayFile
                        val deletedAt = item.deletedAt ?: now
                        val elapsedDays = ((now - deletedAt).coerceAtLeast(0L) / (24L * 60L * 60L * 1000L)).toInt()
                        val remainingDays = (30 - elapsedDays).coerceIn(0, 30)
                        val displayPrice = if (item.status == "SOLD" && item.soldPrice > 0) item.soldPrice else item.price

                        val isSelected = selectedIds.contains(item.id)
                        Card(
                            shape = RoundedCornerShape(14.dp),
                            colors = CardDefaults.cardColors(
                                containerColor = if (isSelected) MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.35f) else MaterialTheme.colorScheme.surface
                            ),
                            elevation = CardDefaults.cardElevation(defaultElevation = 2.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .then(
                                    if (isSelected) Modifier.border(2.dp, MaterialTheme.colorScheme.primary, RoundedCornerShape(14.dp)) else Modifier
                                )
                                .then(
                                    if (isSelectionMode) {
                                        Modifier.clickable {
                                            selectedIds = if (isSelected) selectedIds - item.id else selectedIds + item.id
                                        }
                                    } else Modifier
                                )
                                .testTag("trash_item_card_${item.id}")
                        ) {
                            Column(modifier = Modifier.padding(12.dp)) {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    modifier = Modifier.fillMaxWidth()
                                ) {
                                    if (isSelectionMode) {
                                        Checkbox(
                                            checked = isSelected,
                                            onCheckedChange = { checked ->
                                                selectedIds = if (checked) selectedIds + item.id else selectedIds - item.id
                                            }
                                        )
                                    }
                                    Box(
                                        modifier = Modifier
                                            .size(64.dp)
                                            .clip(RoundedCornerShape(10.dp))
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
                                            Text(
                                                text = "写真なし",
                                                fontSize = 10.sp,
                                                color = MaterialTheme.colorScheme.onSurfaceVariant
                                            )
                                        }
                                    }

                                    Spacer(modifier = Modifier.width(12.dp))

                                    Column(modifier = Modifier.weight(1f)) {
                                        Text(
                                            text = item.title.ifBlank { "無題の商品" },
                                            fontWeight = FontWeight.Bold,
                                            fontSize = 14.sp,
                                            maxLines = 1,
                                            overflow = TextOverflow.Ellipsis,
                                            color = MaterialTheme.colorScheme.onSurface
                                        )
                                        Spacer(modifier = Modifier.height(2.dp))
                                        Row(
                                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                                            verticalAlignment = Alignment.CenterVertically
                                        ) {
                                            Text(
                                                text = formatter.format(displayPrice),
                                                fontWeight = FontWeight.Bold,
                                                fontSize = 13.sp,
                                                color = MaterialTheme.colorScheme.primary
                                            )
                                            Surface(
                                                color = MaterialTheme.colorScheme.errorContainer,
                                                shape = RoundedCornerShape(6.dp)
                                            ) {
                                                Text(
                                                    text = "完全削除まであと ${remainingDays}日",
                                                    fontSize = 10.sp,
                                                    fontWeight = FontWeight.Bold,
                                                    color = MaterialTheme.colorScheme.onErrorContainer,
                                                    modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp)
                                                )
                                            }
                                        }
                                        Spacer(modifier = Modifier.height(2.dp))
                                        Text(
                                            text = "削除日時 ${dateFormat.format(Date(deletedAt))}",
                                            fontSize = 11.sp,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant
                                        )
                                    }
                                }

                                Spacer(modifier = Modifier.height(10.dp))

                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    FilledTonalButton(
                                        onClick = {
                                            viewModel.restoreFromTrash(item.id) {
                                                Toast.makeText(context, "商品を復元しました", Toast.LENGTH_SHORT).show()
                                            }
                                        },
                                        modifier = Modifier
                                            .weight(1f)
                                            .minimumInteractiveComponentSize()
                                            .testTag("restore_trash_item_${item.id}")
                                    ) {
                                        Icon(
                                            imageVector = Icons.Default.RestoreFromTrash,
                                            contentDescription = null,
                                            modifier = Modifier.size(18.dp)
                                        )
                                        Spacer(modifier = Modifier.width(6.dp))
                                        Text("元に戻す", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                                    }

                                    OutlinedButton(
                                        onClick = { confirmPermanentItem = itemWithPhotos },
                                        colors = ButtonDefaults.outlinedButtonColors(
                                            contentColor = MaterialTheme.colorScheme.error
                                        ),
                                        modifier = Modifier
                                            .weight(1f)
                                            .minimumInteractiveComponentSize()
                                            .testTag("permanent_delete_trash_item_${item.id}")
                                    ) {
                                        Icon(
                                            imageVector = Icons.Default.DeleteForever,
                                            contentDescription = null,
                                            modifier = Modifier.size(18.dp)
                                        )
                                        Spacer(modifier = Modifier.width(6.dp))
                                        Text("完全削除", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                                    }
                                }
                            }
                        }
                    }
                }

                if (isSelectionMode && selectedIds.isNotEmpty()) {
                    val targetIds = selectedIds.toList()
                    Surface(
                        shadowElevation = 8.dp,
                        color = MaterialTheme.colorScheme.primaryContainer,
                        modifier = Modifier
                            .fillMaxWidth()
                            .align(Alignment.BottomCenter)
                    ) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 12.dp, vertical = 10.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(
                                text = "${targetIds.size}件選択",
                                fontWeight = FontWeight.Bold,
                                fontSize = 13.sp,
                                color = MaterialTheme.colorScheme.onPrimaryContainer
                            )
                            Spacer(modifier = Modifier.weight(1f))
                            FilledTonalButton(
                                onClick = {
                                    viewModel.batchRestoreFromTrash(targetIds) { count ->
                                        selectedIds = emptySet()
                                        isSelectionMode = false
                                        Toast.makeText(context, "${count}件を復元しました", Toast.LENGTH_SHORT).show()
                                    }
                                },
                                modifier = Modifier.testTag("batch_restore_trash_button")
                            ) {
                                Icon(imageVector = Icons.Default.RestoreFromTrash, contentDescription = null, modifier = Modifier.size(16.dp))
                                Spacer(modifier = Modifier.width(4.dp))
                                Text("一括復元", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                            }
                            Button(
                                onClick = { showBatchPermanentConfirm = true },
                                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                                modifier = Modifier.testTag("batch_permanent_delete_trash_button")
                            ) {
                                Icon(imageVector = Icons.Default.DeleteForever, contentDescription = null, modifier = Modifier.size(16.dp))
                                Spacer(modifier = Modifier.width(4.dp))
                                Text("完全削除", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                            }
                        }
                    }
                }
                }
            }
        }
    }
}
