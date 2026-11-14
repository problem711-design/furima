package com.example.myapplication.ui.screens

import android.widget.Toast
import androidx.compose.ui.draw.alpha
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Clear
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.MergeType
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Sort
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.ui.FleaMarketViewModel
import com.example.myapplication.ui.SortOption
import com.example.myapplication.ui.TimeRange
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material3.OutlinedButton
import com.example.myapplication.ui.components.BatchEditDialog
import com.example.myapplication.ui.components.ItemQuickActionDialog
import com.example.myapplication.ui.components.MergeItemsDialog
import java.io.File
import java.text.NumberFormat
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ItemListScreen(
    viewModel: FleaMarketViewModel,
    onNavigateToDetail: (Int) -> Unit,
    onNavigateToAdd: () -> Unit,
    onNavigateToEdit: (Int) -> Unit = {},
    onNavigateToTrash: () -> Unit = {}
) {
    val context = LocalContext.current
    val haptic = LocalHapticFeedback.current
    val allActiveItems by viewModel.itemsWithPhotos.collectAsState()
    val filteredItems by viewModel.filteredItems.collectAsState()
    val periodSummary by viewModel.periodSalesSummary.collectAsState()
    val defaultSettings by viewModel.defaultSettings.collectAsState()
    val timeRange by viewModel.timeRange.collectAsState()
    val searchQuery by viewModel.searchQuery.collectAsState()
    val statusFilter by viewModel.statusFilter.collectAsState()
    val sortOption by viewModel.sortOption.collectAsState()
    val isSelectionMode by viewModel.isSelectionMode.collectAsState()
    val selectedItemIds by viewModel.selectedItemIds.collectAsState()
    val trashCount by viewModel.trashCount.collectAsState()

    var showSortMenu by remember { mutableStateOf(false) }
    var showMergeDialog by remember { mutableStateOf(false) }
    var showBatchEditDialog by remember { mutableStateOf(false) }
    var showBatchTrashConfirmDialog by remember { mutableStateOf(false) }
    var quickActionItem by remember { mutableStateOf<ItemWithPhotos?>(null) }

    // 表示件数リミット（デフォルト20件、10件ずつ追加可能）
    var displayLimit by remember { mutableIntStateOf(20) }

    // 検索語やフィルターが変わったら表示件数をリセット
    LaunchedEffect(searchQuery, statusFilter, sortOption) {
        displayLimit = 20
    }

    val visibleItems = remember(filteredItems, displayLimit) {
        filteredItems.take(displayLimit)
    }

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)

    // 長押しクイックアクションダイアログ
    quickActionItem?.let { itemWithPhotos ->
        ItemQuickActionDialog(
            itemWithPhotos = itemWithPhotos,
            onDismiss = { quickActionItem = null },
            onToggleStatus = {
                val newStatus = when (itemWithPhotos.item.status) {
                    "SOLD", "DRAFT" -> "LISTED"
                    else -> "SOLD"
                }
                viewModel.updateItemStatus(itemWithPhotos.item, newStatus)
                Toast.makeText(context, if (newStatus == "SOLD") "売却済みに変更しました" else "出品中に変更しました", Toast.LENGTH_SHORT).show()
            },
            onNavigateToEdit = {
                // サムネ長押し→編集選択時にダイレクトに商品編集画面へ遷移
                onNavigateToEdit(itemWithPhotos.item.id)
            },
            onCopyText = {
                viewModel.copyListingTemplate(context, itemWithPhotos)
            },
            onUnmerge = {
                quickActionItem = null
                viewModel.unmergeItem(itemWithPhotos.item.id) {
                    Toast.makeText(context, "まとめ買い出品を解除し、元の個別出品を復元しました", Toast.LENGTH_SHORT).show()
                }
            },
            onDelete = {
                quickActionItem = null
                viewModel.deleteItem(itemWithPhotos) {
                    Toast.makeText(context, "ゴミ箱へ移動しました", Toast.LENGTH_SHORT).show()
                }
            }
        )
    }

    // 複数選択時の一括編集ダイアログ
    if (showBatchEditDialog && selectedItemIds.isNotEmpty()) {
        val selectedItems = allActiveItems.filter { selectedItemIds.contains(it.item.id) }
        if (selectedItems.isNotEmpty()) {
            BatchEditDialog(
                selectedItems = selectedItems,
                defaultSettings = defaultSettings,
                onDismiss = { showBatchEditDialog = false },
                onConfirm = { spec ->
                    showBatchEditDialog = false
                    viewModel.batchUpdateItems(selectedItems.map { it.item.id }, spec) { updatedCount ->
                        Toast.makeText(context, "${updatedCount}件を一括更新しました", Toast.LENGTH_SHORT).show()
                    }
                }
            )
        }
    }

    // 複数選択時の一括ゴミ箱移動確認ダイアログ
    if (showBatchTrashConfirmDialog && selectedItemIds.isNotEmpty()) {
        val selectedIdsList = selectedItemIds.toList()
        AlertDialog(
            onDismissRequest = { showBatchTrashConfirmDialog = false },
            icon = { Icon(imageVector = Icons.Default.DeleteOutline, contentDescription = null, tint = MaterialTheme.colorScheme.error) },
            title = { Text("選択した商品をゴミ箱へ移動") },
            text = { Text("選択中の ${selectedIdsList.size}件の商品をゴミ箱へ移動しますか？（30日間保持され、いつでも元に戻せます）") },
            confirmButton = {
                Button(
                    onClick = {
                        showBatchTrashConfirmDialog = false
                        viewModel.batchMoveToTrash(selectedIdsList) { movedCount ->
                            Toast.makeText(context, "${movedCount}件をゴミ箱へ移動しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                    modifier = Modifier.testTag("confirm_batch_trash_button")
                ) {
                    Text("ゴミ箱へ移動")
                }
            },
            dismissButton = {
                TextButton(onClick = { showBatchTrashConfirmDialog = false }) {
                    Text("キャンセル")
                }
            }
        )
    }

    // 複数選択時のまとめ買いダイアログ
    if (showMergeDialog && selectedItemIds.size >= 2) {
        val selectedItems = allActiveItems.filter { selectedItemIds.contains(it.item.id) }
        MergeItemsDialog(
            selectedItems = selectedItems,
            defaultSettings = defaultSettings,
            onDismiss = { showMergeDialog = false },
            onConfirm = { title, desc, price, shipping, feeRate, expense, itemIds ->
                viewModel.mergeSelectedItems(
                    mergedTitle = title,
                    mergedDescription = desc,
                    totalSoldPrice = price,
                    newShippingCost = shipping,
                    newFeeRatePercent = feeRate,
                    newExpenseCost = expense,
                    childItemIds = itemIds,
                    asStatus = "LISTED"
                ) { _ ->
                    showMergeDialog = false
                    Toast.makeText(context, "まとめ出品を作成しました！", Toast.LENGTH_SHORT).show()
                }
            }
        )
    }

    Scaffold(
        topBar = {
            // スリム化したTopAppBar
            Surface(
                color = MaterialTheme.colorScheme.primary,
                shadowElevation = 2.dp
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 14.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        text = if (isSelectionMode) "選択中 (${selectedItemIds.size}件)" else "フリマ出品・売上管理",
                        fontWeight = FontWeight.Bold,
                        fontSize = 17.sp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )

                    if (isSelectionMode) {
                        TextButton(
                            onClick = { viewModel.clearSelection() },
                            colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.onPrimary)
                        ) {
                            Text("完了", fontWeight = FontWeight.Bold)
                        }
                    } else {
                        IconButton(
                            onClick = onNavigateToTrash,
                            modifier = Modifier
                                .minimumInteractiveComponentSize()
                                .testTag("open_trash_button")
                        ) {
                            BadgedBox(
                                badge = {
                                    if (trashCount > 0) {
                                        Badge(
                                            containerColor = MaterialTheme.colorScheme.error,
                                            contentColor = MaterialTheme.colorScheme.onError
                                        ) {
                                            Text(if (trashCount > 99) "99+" else trashCount.toString())
                                        }
                                    }
                                }
                            ) {
                                Icon(
                                    imageVector = Icons.Default.DeleteOutline,
                                    contentDescription = "ゴミ箱を開く",
                                    tint = MaterialTheme.colorScheme.onPrimary
                                )
                            }
                        }
                    }
                }
            }
        },
        floatingActionButton = {
            if (!isSelectionMode) {
                FloatingActionButton(
                    onClick = onNavigateToAdd,
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.testTag("add_item_fab")
                ) {
                    Icon(imageVector = Icons.Default.Add, contentDescription = "新規追加")
                }
            }
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
        ) {
            // ① スリム化された上部サマリーバー
            Surface(
                color = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.4f),
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 12.dp, vertical = 4.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // 期間トグル [ 今週 | 今月 | 全期間 ]
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        TimeRange.values().forEach { range ->
                            val isSelected = timeRange == range
                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(if (isSelected) MaterialTheme.colorScheme.primary else Color.Transparent)
                                    .clickable {
                                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                        viewModel.setTimeRange(range)
                                    }
                                    .padding(horizontal = 7.dp, vertical = 2.dp)
                            ) {
                                Text(
                                    text = range.label,
                                    fontSize = 11.sp,
                                    fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                    color = if (isSelected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }

                    // 1行集計（売上と純利益）
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = "売上: ${formatter.format(periodSummary.totalSales)}",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Text(
                            text = " | 利益: ",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Text(
                            text = formatter.format(periodSummary.totalProfit),
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.primary
                        )
                    }
                }
            }

            // ② 検索バー & ソートメニュー & 表示切り替え
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 10.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { viewModel.setSearchQuery(it) },
                    placeholder = { Text("検索 (スペースでAND, OR, \"完全一致\")", fontSize = 11.sp) },
                    textStyle = androidx.compose.ui.text.TextStyle(fontSize = 12.sp),
                    leadingIcon = { Icon(Icons.Default.Search, contentDescription = null, modifier = Modifier.size(16.dp)) },
                    trailingIcon = {
                        if (searchQuery.isNotBlank()) {
                            IconButton(onClick = { viewModel.setSearchQuery("") }) {
                                Icon(Icons.Default.Clear, contentDescription = "クリア", modifier = Modifier.size(14.dp))
                            }
                        }
                    },
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedContainerColor = MaterialTheme.colorScheme.surface,
                        unfocusedContainerColor = MaterialTheme.colorScheme.surface
                    ),
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp)
                        .testTag("search_input")
                )

                Spacer(modifier = Modifier.width(4.dp))

                // まとめ買い選択ボタントグル
                IconButton(
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        viewModel.toggleSelectionMode()
                    },
                    modifier = Modifier
                        .size(38.dp)
                        .background(
                            if (isSelectionMode) MaterialTheme.colorScheme.primaryContainer else Color.Transparent,
                            shape = CircleShape
                        )
                        .testTag("search_merge_toggle")
                ) {
                    Icon(
                        imageVector = Icons.Default.MergeType,
                        contentDescription = "まとめ買い選択切替",
                        tint = if (isSelectionMode) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.size(18.dp)
                    )
                }

                // ソートボタンドロップダウン
                Box {
                    IconButton(
                        onClick = { showSortMenu = true },
                        modifier = Modifier.size(38.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Default.Sort,
                            contentDescription = "ソート",
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.size(18.dp)
                        )
                    }

                    DropdownMenu(
                        expanded = showSortMenu,
                        onDismissRequest = { showSortMenu = false }
                    ) {
                        SortOption.values().forEach { option ->
                            DropdownMenuItem(
                                text = {
                                    Text(
                                        text = option.label,
                                        fontWeight = if (sortOption == option) FontWeight.Bold else FontWeight.Normal,
                                        color = if (sortOption == option) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                                        fontSize = 13.sp
                                    )
                                },
                                onClick = {
                                    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                    viewModel.setSortOption(option)
                                    showSortMenu = false
                                }
                            )
                        }
                    }
                }
            }

            // ③ ステータスフィルターチップ
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 10.dp, vertical = 2.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                FilterChip(
                    selected = (statusFilter == "ALL"),
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        viewModel.setStatusFilter("ALL")
                    },
                    label = { Text("すべて (${filteredItems.size})", fontSize = 11.sp) },
                    modifier = Modifier.height(28.dp)
                )
                FilterChip(
                    selected = (statusFilter == "DRAFT"),
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        viewModel.setStatusFilter("DRAFT")
                    },
                    label = { Text("下書き", fontSize = 11.sp) },
                    modifier = Modifier.height(28.dp)
                )
                FilterChip(
                    selected = (statusFilter == "LISTED"),
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        viewModel.setStatusFilter("LISTED")
                    },
                    label = { Text("出品中", fontSize = 11.sp) },
                    modifier = Modifier.height(28.dp)
                )
                FilterChip(
                    selected = (statusFilter == "SOLD"),
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        viewModel.setStatusFilter("SOLD")
                    },
                    label = { Text("売却済", fontSize = 11.sp) },
                    modifier = Modifier.height(28.dp)
                )

                Spacer(modifier = Modifier.weight(1f))
                Text(
                    text = "${visibleItems.size} / ${filteredItems.size}件",
                    fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f)
                )
            }

            Spacer(modifier = Modifier.height(4.dp))

            // ④ 一覧表示（コンパクトな2列グリッド ＆ 折りたたみボタン）
            if (filteredItems.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text("該当する商品がありません", color = Color.Gray)
                }
            } else {
                Box(modifier = Modifier.weight(1f)) {
                    LazyVerticalGrid(
                        columns = GridCells.Fixed(2),
                        contentPadding = PaddingValues(8.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                        modifier = Modifier.fillMaxSize()
                    ) {
                        items(visibleItems, key = { it.item.id }) { itemWithPhotos ->
                            val isSelected = selectedItemIds.contains(itemWithPhotos.item.id)
                            CompactItemCard(
                                itemWithPhotos = itemWithPhotos,
                                defaultFeePercent = defaultSettings.defaultFeeRatePercent,
                                defaultShipping = defaultSettings.defaultShippingCost,
                                defaultExpense = defaultSettings.defaultExpenseCost,
                                isSelectionMode = isSelectionMode,
                                isSelected = isSelected,
                                onSelectToggle = {
                                    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                    viewModel.toggleItemSelection(itemWithPhotos.item.id)
                                },
                                onClick = {
                                    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                    if (isSelectionMode) {
                                        viewModel.toggleItemSelection(itemWithPhotos.item.id)
                                    } else {
                                        onNavigateToDetail(itemWithPhotos.item.id)
                                    }
                                },
                                onLongClick = {
                                    if (!isSelectionMode) {
                                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                                        quickActionItem = itemWithPhotos
                                    }
                                }
                            )
                        }

                        // 表示件数が全件未満の場合の「さらに10件表示」ボタン
                        if (filteredItems.size > displayLimit) {
                            item(span = { GridItemSpan(2) }) {
                                Column(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(vertical = 12.dp),
                                    horizontalAlignment = Alignment.CenterHorizontally
                                ) {
                                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                        Button(
                                            onClick = { displayLimit += 10 },
                                            colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.secondaryContainer, contentColor = MaterialTheme.colorScheme.onSecondaryContainer),
                                            shape = RoundedCornerShape(20.dp)
                                        ) {
                                            Icon(Icons.Default.ExpandMore, contentDescription = null, modifier = Modifier.size(16.dp))
                                            Spacer(modifier = Modifier.width(4.dp))
                                            Text("さらに10件表示 (${displayLimit}/${filteredItems.size}件)", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                                        }

                                        OutlinedButton(
                                            onClick = { displayLimit = filteredItems.size },
                                            shape = RoundedCornerShape(20.dp)
                                        ) {
                                            Text("全件表示", fontSize = 12.sp)
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 複数選択時の下部一括操作アクションバー (1件以上で一括編集・ゴミ箱／2件以上かつ未売却でまとめ)
                    if (isSelectionMode && selectedItemIds.isNotEmpty()) {
                        val selectedItemsList = allActiveItems.filter { selectedItemIds.contains(it.item.id) }
                        val selectedSubItemCount = selectedItemsList.sumOf { if (it.item.mergedChildCount > 0) it.item.mergedChildCount else 1 }
                        val canMerge = selectedItemsList.size >= 2 && selectedItemsList.all {
                            it.item.status == "LISTED" || it.item.status == "DRAFT"
                        }
                        Surface(
                            shadowElevation = 8.dp,
                            color = MaterialTheme.colorScheme.primaryContainer,
                            modifier = Modifier
                                .fillMaxWidth()
                                .align(Alignment.BottomCenter)
                        ) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 12.dp, vertical = 10.dp),
                                verticalArrangement = Arrangement.spacedBy(6.dp)
                            ) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Text(
                                        text = "${selectedItemsList.size}件 (${selectedSubItemCount}点) 選択中",
                                        fontWeight = FontWeight.Bold,
                                        fontSize = 13.sp,
                                        color = MaterialTheme.colorScheme.onPrimaryContainer
                                    )
                                    if (selectedItemsList.size >= 2 && !canMerge) {
                                        Text(
                                            text = "※売却済を含むためまとめ不可",
                                            fontSize = 10.sp,
                                            color = MaterialTheme.colorScheme.error
                                        )
                                    }
                                }
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Button(
                                        onClick = { showBatchEditDialog = true },
                                        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 6.dp),
                                        modifier = Modifier
                                            .weight(1f)
                                            .testTag("batch_edit_action_button")
                                    ) {
                                        Icon(imageVector = Icons.Default.Edit, contentDescription = null, modifier = Modifier.size(15.dp))
                                        Spacer(modifier = Modifier.width(4.dp))
                                        Text("一括編集", fontWeight = FontWeight.Bold, fontSize = 12.sp, maxLines = 1)
                                    }
                                    Button(
                                        onClick = { showBatchTrashConfirmDialog = true },
                                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                                        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 6.dp),
                                        modifier = Modifier
                                            .weight(1f)
                                            .testTag("batch_trash_action_button")
                                    ) {
                                        Icon(imageVector = Icons.Default.DeleteOutline, contentDescription = null, modifier = Modifier.size(15.dp))
                                        Spacer(modifier = Modifier.width(4.dp))
                                        Text("ゴミ箱へ", fontWeight = FontWeight.Bold, fontSize = 12.sp, maxLines = 1)
                                    }
                                    Button(
                                        onClick = { showMergeDialog = true },
                                        enabled = canMerge,
                                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.tertiary),
                                        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 6.dp),
                                        modifier = Modifier
                                            .weight(1f)
                                            .testTag("merge_action_button")
                                    ) {
                                        Icon(imageVector = Icons.Default.MergeType, contentDescription = null, modifier = Modifier.size(15.dp))
                                        Spacer(modifier = Modifier.width(4.dp))
                                        Text("まとめる", fontWeight = FontWeight.Bold, fontSize = 12.sp, maxLines = 1)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun CompactItemCard(
    itemWithPhotos: ItemWithPhotos,
    defaultFeePercent: Int,
    defaultShipping: Int,
    defaultExpense: Int,
    isSelectionMode: Boolean,
    isSelected: Boolean,
    onSelectToggle: () -> Unit,
    onClick: () -> Unit,
    onLongClick: () -> Unit = {}
) {
    val item = itemWithPhotos.item
    val photos = itemWithPhotos.photos
    val firstPhoto = photos.firstOrNull()

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)
    val basePrice = if (item.status == "SOLD" && item.soldPrice > 0) item.soldPrice else item.price

    val effectiveFee = if (item.useCustomSettings) item.feeRatePercent else defaultFeePercent
    val effectiveShipping = if (item.useCustomSettings) item.shippingCost else defaultShipping
    val effectiveExpense = if (item.useCustomSettings) item.expenseCost else defaultExpense
    val fee = (basePrice * effectiveFee) / 100
    val profit = (basePrice - fee - effectiveShipping - effectiveExpense).coerceAtLeast(0)

    val isBundle = item.mergedChildCount > 0 || item.bundleLevel > 0
    val isSelectable = item.status != "MERGED" && item.deletedAt == null

    val displayImageFile = firstPhoto?.displayFile

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .then(if (isSelectionMode && !isSelectable) Modifier.alpha(0.5f) else Modifier)
            .combinedClickable(
                onClick = onClick,
                onLongClick = onLongClick
            )
            .border(
                width = if (isSelected) 2.dp else 0.dp,
                color = if (isSelected) MaterialTheme.colorScheme.primary else Color.Transparent,
                shape = RoundedCornerShape(12.dp)
            )
            .testTag("item_card_${item.id}"),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isSelected) MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.3f) else MaterialTheme.colorScheme.surface
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Column {
            // 写真エリア
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .aspectRatio(1.2f)
                    .background(Color.LightGray)
            ) {
                if (displayImageFile != null && displayImageFile.exists()) {
                    AsyncImage(
                        model = displayImageFile,
                        contentDescription = item.title,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize()
                    )
                } else {
                    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text("No Photo", color = Color.DarkGray, fontSize = 11.sp)
                    }
                }

                // ステータスバッジ ＆ まとめバッジ
                val hasUnsynced = photos.any { !it.isPcSynced && java.io.File(it.filePath).exists() }
                val isAllSynced = photos.isNotEmpty() && photos.all { it.isPcSynced }

                Row(
                    modifier = Modifier
                        .align(Alignment.TopStart)
                        .padding(4.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(4.dp))
                            .background(
                                when (item.status) {
                                    "SOLD" -> Color(0xFFC62828)
                                    "MERGED" -> Color(0xFF6A1B9A)
                                    "DRAFT" -> Color.Gray
                                    else -> MaterialTheme.colorScheme.primary
                                }
                            )
                            .padding(horizontal = 6.dp, vertical = 2.dp)
                    ) {
                        Text(
                            text = when (item.status) {
                                "SOLD" -> "売却済"
                                "MERGED" -> "まとめ済"
                                "DRAFT" -> "下書き"
                                else -> "出品中"
                            },
                            color = Color.White,
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }

                    if (hasUnsynced) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(4.dp))
                                .background(Color(0xFF4F46E5))
                                .padding(horizontal = 5.dp, vertical = 2.dp)
                        ) {
                            Text(text = "未同期", color = Color.White, fontSize = 9.sp, fontWeight = FontWeight.Bold)
                        }
                    } else if (isAllSynced) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(4.dp))
                                .background(Color(0xFF0284C7))
                                .padding(horizontal = 5.dp, vertical = 2.dp)
                        ) {
                            Text(text = "送信済み", color = Color.White, fontSize = 9.sp, fontWeight = FontWeight.Bold)
                        }
                    }

                    if (isBundle) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(4.dp))
                                .background(Color(0xFFE65100))
                                .padding(horizontal = 5.dp, vertical = 2.dp)
                        ) {
                            Text(
                                text = "まとめ (${item.mergedChildCount}点)",
                                color = Color.White,
                                fontSize = 10.sp,
                                fontWeight = FontWeight.Bold
                            )
                        }
                    }
                }

                // 写真枚数インジケーター
                if (photos.size > 1) {
                    Box(
                        modifier = Modifier
                            .padding(4.dp)
                            .clip(RoundedCornerShape(4.dp))
                            .background(Color.Black.copy(alpha = 0.6f))
                            .padding(horizontal = 5.dp, vertical = 2.dp)
                            .align(Alignment.TopEnd)
                    ) {
                        Text(
                            text = "${photos.size}枚",
                            color = Color.White,
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Medium
                        )
                    }
                }
            }

            // 情報エリア
            Column(modifier = Modifier.padding(8.dp)) {
                Text(
                    text = item.title.ifBlank { "無題の商品" },
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )

                Spacer(modifier = Modifier.height(2.dp))

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = formatter.format(basePrice),
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary
                    )
                    Text(
                        text = "利益: ${formatter.format(profit)}",
                        fontSize = 11.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color(0xFF2E7D32)
                    )
                }

                if (item.useCustomSettings) {
                    Text(
                        text = "個別設定: 手数料${effectiveFee}% / 送料${effectiveShipping}円",
                        fontSize = 9.sp,
                        color = Color.Gray,
                        maxLines = 1
                    )
                }
            }
        }
    }
}
